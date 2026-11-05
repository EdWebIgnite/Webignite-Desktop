using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Webignite.Core.Projects;

namespace Webignite.Desktop.ViewModels;

// Settings, Projects: you and your partner. Names can change any time; who did what is kept by id.
public partial class SettingsViewModel
{
    [ObservableProperty] public partial string MyName { get; set; } = "";
    [ObservableProperty] public partial string PartnerName { get; set; } = "";
    [ObservableProperty] public partial string PartnerEmail { get; set; } = "";
    [ObservableProperty] public partial string TeamMessage { get; private set; } = "";
    [ObservableProperty] public partial bool TeamProblem { get; private set; }

    private void LoadTeam()
    {
        var team = services.Team();
        MyName = team.Me!.Name;
        PartnerName = team.Partner!.Name;
        PartnerEmail = team.Partner.Email ?? "";
    }

    [RelayCommand]
    private void SaveTeam()
    {
        var team = services.Team();
        var me = team.Me! with { Name = MyName.Trim() };
        var partner = team.Partner! with { Name = PartnerName.Trim(), Email = PartnerEmail.Trim() is { Length: > 0 } email ? email : null };
        var problems = ProjectRules.Check(me, partner);
        if (problems.Count > 0)
        {
            TeamMessage = string.Join(" ", problems);
            TeamProblem = true;
            return;
        }
        services.Settings.Team = team with { Me = me, Partner = partner };
        TeamMessage = "Saved.";
        TeamProblem = false;
    }
}
