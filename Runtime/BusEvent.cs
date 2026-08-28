using System;
using System.Collections.Generic;

namespace Nopnag.EventBusLib // Updated namespace
{

  // Interface for parameter types, assuming it's needed by BusEvent
  public interface IParameter {}

  public class BusEvent
  {
    readonly Dictionary<Type, object> _dict;
    readonly Dictionary<Type, object> _genericDict;

    public BusEvent()
    {
      _dict = new Dictionary<Type, object>();
      _genericDict = new Dictionary<Type, object>();
    }

    // A new unique identifier assigned per raise operation.
    // Readable publicly, but only settable within this assembly (library-internal).
    public long RaiseUniqueId { get; internal set; }

    // Internal raise depth tracking to ensure RaiseUniqueId is assigned only once
    // for the outermost raise call, while nested/forwarded raises share the same ID.
    internal int ActiveRaiseDepth { get; set; }

    public virtual bool IsPropagationStopped { get; set; }

    public object Get<T>() where T : IParameter
    {
      object value;
      if (_dict.TryGetValue(typeof(T), out value)) return value;

      return default(T);
    }

    public T GetGeneric<T>() where T : class
    {
      object value;
      if (_genericDict.TryGetValue(typeof(T), out value)) return (T)value;

      return default;
    }

    /// <summary>
    /// Clears the propagation-stopped flag. EventBus calls this automatically at
    /// the start of every top-level raise; manual calls are only needed when changing
    /// propagation state outside normal dispatch.
    /// </summary>
    public virtual void ResetPropagation()
    {
      IsPropagationStopped = false;
    }

    public BusEvent Set<T>(T value) where T : class
    {
      _genericDict[typeof(T)] = value;
      return this;
    }

    /// <summary>
    /// Sets a queryable parameter value. Because the value is stored as object, passing
    /// a value type such as int, float, bool, or an enum boxes that value and may allocate
    /// on every call. For hot or reusable events, keep gameplay payload in strongly typed
    /// event fields and use this API only for values that are actually needed for routing.
    /// A cached boxed value or stable reference token can be used when value-based routing
    /// is required without repeated boxing.
    /// </summary>
    public BusEvent Set<T>(object value) where T : IParameter
    {
      _dict[typeof(T)] = value;
      return this;
    }

    public void StopPropagation()
    {
      IsPropagationStopped = true;
    }
  }
}
