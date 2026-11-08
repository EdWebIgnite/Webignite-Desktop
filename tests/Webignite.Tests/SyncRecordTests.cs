using System.Security.Cryptography;
using Webignite.Core.Projects;
using Webignite.Core.Sync;

namespace Webignite.Tests;

public sealed class SyncRecordTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    private static Stamp At(string by, int minutes) => new(by, Start.AddMinutes(minutes));

    [Fact]
    public void A_locked_record_opens_only_with_its_key_kind_and_id()
    {
        var key = RecordLock.KeyFrom(RandomNumberGenerator.GetBytes(32));
        var locked = RecordLock.Seal(SyncKinds.Task, "t1", "{\"Title\":\"Hi\"}", key);
        Assert.Equal("{\"Title\":\"Hi\"}", RecordLock.Open(SyncKinds.Task, "t1", locked, key));
        Assert.Null(RecordLock.Open(SyncKinds.Task, "t2", locked, key));
        Assert.Null(RecordLock.Open(SyncKinds.Project, "t1", locked, key));
        Assert.Null(RecordLock.Open(SyncKinds.Task, "t1", locked, RecordLock.KeyFrom(RandomNumberGenerator.GetBytes(32))));
        Assert.Null(RecordLock.Open(SyncKinds.Task, "t1", "not base64!", key));
        var bytes = Convert.FromBase64String(locked);
        bytes[^1] ^= 1;
        Assert.Null(RecordLock.Open(SyncKinds.Task, "t1", Convert.ToBase64String(bytes), key));
    }

    [Fact]
    public void The_fingerprint_ignores_the_order_properties_come_in()
    {
        Assert.Equal(RecordLock.Fingerprint("{\"a\":1,\"b\":{\"y\":2,\"x\":3}}"), RecordLock.Fingerprint("{\"b\":{\"x\":3,\"y\":2},\"a\":1}"));
        Assert.NotEqual(RecordLock.Fingerprint("{\"a\":1}"), RecordLock.Fingerprint("{\"a\":2}"));
        Assert.NotEqual(RecordLock.Fingerprint("{\"a\":[1,2]}"), RecordLock.Fingerprint("{\"a\":[2,1]}"));
    }

    [Fact]
    public void Merging_keeps_each_fields_later_change()
    {
        var created = At("eddie", 0);
        var local = new ProjectTask
        {
            Id = "t1", ProjectId = "p1", Title = "New title", Due = new DateOnly(2026, 11, 1), Done = false,
            Created = created, Changed = At("eddie", 5),
            Fields = new Dictionary<string, Stamp> { ["title"] = At("eddie", 5), ["due"] = At("eddie", 1) },
        };
        var remote = local with
        {
            Title = "Old title", Due = new DateOnly(2026, 11, 20), Done = true,
            Changed = At("lize", 7),
            Fields = new Dictionary<string, Stamp> { ["title"] = At("lize", 2), ["due"] = At("lize", 6), ["done"] = At("lize", 7) },
        };

        var merged = RecordMerge.Merge(local, remote);
        Assert.Equal("New title", merged.Title);
        Assert.Equal(new DateOnly(2026, 11, 20), merged.Due);
        Assert.True(merged.Done);
        Assert.Equal(At("lize", 7), merged.Changed);
        Assert.Equal(At("eddie", 5), merged.Fields["title"]);
        Assert.Equal(At("lize", 6), merged.Fields["due"]);

        // Both PCs get the same answer whichever way round they merge.
        var other = RecordMerge.Merge(remote, local);
        Assert.Equal(merged.Title, other.Title);
        Assert.Equal(merged.Due, other.Due);
        Assert.Equal(merged.Done, other.Done);
        Assert.Equal(merged.Changed, other.Changed);
    }

    [Fact]
    public void A_deletion_stays_unless_the_other_side_changed_it_afterwards()
    {
        var kept = new Heading { Id = "h1", ProjectId = "p1", Name = "Build", Created = At("eddie", 0), Changed = At("eddie", 3) };
        var deleted = kept with { Deleted = At("lize", 5), Changed = At("lize", 5) };
        Assert.Equal(At("lize", 5), RecordMerge.Merge(kept, deleted).Deleted);
        Assert.Equal(At("lize", 5), RecordMerge.Merge(deleted, kept).Deleted);

        var editedLater = kept with { Name = "Build and test", Changed = At("eddie", 9), Fields = new Dictionary<string, Stamp> { ["name"] = At("eddie", 9) } };
        var merged = RecordMerge.Merge(editedLater, deleted);
        Assert.Null(merged.Deleted);
        Assert.Equal("Build and test", merged.Name);
    }

    [Fact]
    public void Team_ids_on_the_partners_pc_map_to_the_owners()
    {
        var shared = new TeamRecord(new Teammate { Id = "owner", Name = "Eddie" }, new Teammate { Id = "member", Name = "Partner" });
        var local = new TeamSettings { Me = new Teammate { Id = "lize-own", Name = "Lize" }, Partner = new Teammate { Id = "eddie-guess", Name = "Partner" } };
        var map = TeamIds.Map(local, shared.Member, shared.Owner);
        Assert.Equal("member", map["lize-own"]);
        Assert.Equal("owner", map["eddie-guess"]);
        Assert.Equal("{\"AssignedTo\":\"member\",\"By\":\"owner\",\"Title\":\"lize-own's\"}",
            TeamIds.Remap("{\"AssignedTo\":\"lize-own\",\"By\":\"eddie-guess\",\"Title\":\"lize-own's\"}", map));
        Assert.Empty(TeamIds.Map(new TeamSettings { Me = shared.Member, Partner = shared.Owner }, shared.Member, shared.Owner));

        var taken = TeamIds.TakeOn(local, shared.Member, shared.Owner);
        Assert.Equal(new Teammate { Id = "member", Name = "Lize" }, taken.Me);
        Assert.Equal(new Teammate { Id = "owner", Name = "Eddie" }, taken.Partner);
        // A partner already named on this PC keeps that name.
        var named = local with { Partner = local.Partner! with { Name = "Ed" } };
        Assert.Equal("Ed", TeamIds.TakeOn(named, shared.Member, shared.Owner).Partner!.Name);
    }
}

public sealed class SyncSummaryTests
{
    [Fact]
    public void The_summary_says_what_moved_and_what_was_left_out()
    {
        Assert.Equal("Synced. Nothing new either way.", new Webignite.Services.SyncResult().Summary());
        Assert.Equal("Synced. One change came in from your partner.", new Webignite.Services.SyncResult(Changed: 1).Summary());
        Assert.Equal("Synced. 3 changes came in and 2 changes went out.", new Webignite.Services.SyncResult(Changed: 3, Sent: 2).Summary());
        Assert.Contains("couldn't be unlocked", new Webignite.Services.SyncResult(Unreadable: 2).Summary(), StringComparison.Ordinal);
        Assert.Contains("owner's PC", new Webignite.Services.SyncResult(WaitingForOwner: true).Summary(), StringComparison.Ordinal);
        Assert.Equal("Sign in again.", new Webignite.Services.SyncResult(Problem: "Sign in again.").Summary());
    }
}
