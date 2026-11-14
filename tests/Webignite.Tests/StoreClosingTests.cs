using System.Security.Cryptography;
using Webignite.Core.Clients;
using Webignite.Data;

namespace Webignite.Tests;

public sealed class StoreClosingTests
{
    [Fact]
    public async Task Closing_the_store_while_another_thread_is_reading_it_does_not_throw()
    {
        // The app counts things for badges on other threads; closing must not trip over a read in flight.
        for (var round = 0; round < 40; round++)
        {
            var folder = Path.Combine(Path.GetTempPath(), "webignite-closing-" + Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(folder);
            var store = new LocalStore(Path.Combine(folder, "test.db"), new Vault(RandomNumberGenerator.GetBytes(32)));
            for (var i = 0; i < 20; i++)
                store.SaveClient(new Client { Id = "c" + i, Name = "Client " + i });
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancel = TestContext.Current.CancellationToken;
            var reading = Task.Run(() =>
            {
                started.SetResult();
                try
                {
                    for (var i = 0; i < 200; i++)
                        store.ListClients();
                }
                catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException or NullReferenceException)
                {
                    // The reader losing its connection mid-read is expected; the closer throwing is not.
                }
            }, cancel);
            await started.Task.WaitAsync(cancel);

            var closing = Record.Exception(store.Dispose);

            Assert.Null(closing);
            await reading.WaitAsync(TimeSpan.FromSeconds(10), cancel);
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
