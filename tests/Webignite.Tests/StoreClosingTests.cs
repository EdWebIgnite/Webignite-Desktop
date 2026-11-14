using System.Security.Cryptography;
using Webignite.Core.Clients;
using Webignite.Data;

namespace Webignite.Tests;

public sealed class StoreClosingTests
{
    [Fact]
    public async Task Closing_the_store_while_another_thread_is_reading_it_harms_neither()
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
            Exception? readerSaw = null;
            var reading = Task.Run(() =>
            {
                started.SetResult();
                try
                {
                    for (var i = 0; i < 400; i++)
                    {
                        // Each read must be whole or refused: twenty clients, or "the store is closed".
                        if (store.ListClients().Count != 20)
                            throw new InvalidDataException("A read came back short.");
                        store.GetClient("c" + i % 20);
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Refused because the store is closing: the one thing a reader may be told.
                }
                catch (Exception e)
                {
                    readerSaw = e;
                }
            }, cancel);
            await started.Task.WaitAsync(cancel);

            var closing = Record.Exception(store.Dispose);

            Assert.Null(closing);
            await reading.WaitAsync(TimeSpan.FromSeconds(10), cancel);
            // And the reader was never handed rubbish: it finished, or was told the store had closed.
            Assert.Null(readerSaw);
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
