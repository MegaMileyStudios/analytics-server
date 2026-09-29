namespace Aptabase.Features.ViewTracking.Buffer;

public interface IViewBuffer
{
    void AddRange(IEnumerable<TrackingView> views);
    TrackingView[] TakeAll();
    int Count();
}

public class InMemoryViewBuffer : IViewBuffer
{
    private readonly List<TrackingView> _buffer = new();
    private readonly object _lock = new();

    public void AddRange(IEnumerable<TrackingView> views)
    {
        lock (_lock)
        {
            _buffer.AddRange(views);
        }
    }

    public TrackingView[] TakeAll()
    {
        lock (_lock)
        {
            var all = _buffer.ToArray();
            _buffer.Clear();
            return all;
        }
    }

    public int Count()
    {
        lock (_lock)
        {
            return _buffer.Count;
        }
    }
}
