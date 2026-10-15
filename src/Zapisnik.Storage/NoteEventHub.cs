using System.Collections.Immutable;
using System.Threading.Channels;
using Zapisnik.Core.Events;

namespace Zapisnik.Storage;

/// <summary>Fan-out of <see cref="NoteEvent"/>s to independent consumers (indexer, UI, sync).</summary>
public sealed class NoteEventHub
{
    private ImmutableArray<Channel<NoteEvent>> _subscribers = [];

    public ChannelReader<NoteEvent> Subscribe()
    {
        var channel = Channel.CreateUnbounded<NoteEvent>(new UnboundedChannelOptions { SingleReader = true });
        ImmutableInterlocked.Update(ref _subscribers, s => s.Add(channel));
        return channel.Reader;
    }

    public void Unsubscribe(ChannelReader<NoteEvent> reader)
    {
        ImmutableInterlocked.Update(ref _subscribers, s => s.RemoveAll(c => c.Reader == reader));
    }

    public void Publish(NoteEvent noteEvent)
    {
        foreach (var subscriber in _subscribers)
        {
            subscriber.Writer.TryWrite(noteEvent);
        }
    }

    public void Complete()
    {
        foreach (var subscriber in _subscribers)
        {
            subscriber.Writer.TryComplete();
        }
    }
}
