using System;
using System.Collections.Generic;
using System.Threading;

namespace Nopnag.EventBusLib // Updated namespace
{
  public delegate void ListenerDelegate<T>(T @event);

  public interface IIListener
  {
    void Unsubscribe();
  }

  public struct Listener : IIListener
  {
    readonly Action _unsubscribeAction;

    public Listener(Action unsubscribeAction)
    {
      _unsubscribeAction = unsubscribeAction;
    }

    public void Unsubscribe()
    {
      _unsubscribeAction();
    }
  }

  // Static EventBus API (unchanged for backward compatibility)
  public static class EventBus
  {
    static long _raiseIdCounter;

    internal static long NextRaiseUniqueId()
    {
      return Interlocked.Increment(ref _raiseIdCounter);
    }

    public static EventQuery<TEvent> Query<TEvent>() where TEvent : BusEvent
    {
      return EventBus<TEvent>.SelfQuery;
    }

    /// <summary>
    /// Raises an event globally. The outermost dispatch resets propagation and
    /// assigns a new RaiseUniqueId before invoking listeners.
    /// </summary>
    public static void Raise<TEvent>(TEvent busEvent) where TEvent : BusEvent
    {
      EventBus<TEvent>.Raise(busEvent);
    }
  }

  public static class EventBus<T> where T : BusEvent
  {
    public static EventQuery<T> SelfQuery;

    static EventBus()
    {
      if (SelfQuery == null) SelfQuery = new EventQuery<T>();
    }

    public static IIListener Listen(ListenerDelegate<T> listener)
    {
      return SelfQuery.Listen(listener);
    }

    /// <summary>
    /// Raises an event globally. The outermost dispatch resets propagation and
    /// assigns a new RaiseUniqueId before invoking listeners.
    /// </summary>
    public static void Raise(T @event)
    {
      if (SelfQuery == null) SelfQuery = new EventQuery<T>();
      SelfQuery.Raise(@event);
    }

    public static EventQuery<T> Where<TParameterType>(in object parameter)
      where TParameterType : IParameter
    {
      return SelfQuery.Where<TParameterType>(parameter);
    }

    public static EventQuery<T> Where<TParameterType>(in TParameterType parameter)
      where TParameterType : class
    {
      return SelfQuery.Where(parameter);
    }
  }

  // New instance-based LocalEventBus
  public class LocalEventBus
  {
    private readonly Dictionary<Type, object> _eventQueries = new Dictionary<Type, object>();

    public LocalEventBus()
    {
    }

    // Instance API
    public EventQuery<TEvent> On<TEvent>() where TEvent : BusEvent
    {
      var eventType = typeof(TEvent);
      if (!_eventQueries.ContainsKey(eventType))
      {
        _eventQueries[eventType] = new EventQuery<TEvent>();
      }
      return (EventQuery<TEvent>)_eventQueries[eventType];
    }

    /// <summary>
    /// Raises an event on this local bus. The outermost dispatch resets propagation
    /// and assigns a new RaiseUniqueId before invoking listeners.
    /// </summary>
    public void Raise<TEvent>(TEvent busEvent) where TEvent : BusEvent
    {
      On<TEvent>().Raise(busEvent);
    }
  }

  public class EventQuery<T> where T : BusEvent
  {
    const int InitialCapacity = 4;

    enum PendingOperationType : byte
    {
      Subscribe,
      Unsubscribe
    }

    struct PendingOperation
    {
      public PendingOperationType Type;
      public ListenerDelegate<T> Listener;

      public PendingOperation(PendingOperationType type, ListenerDelegate<T> listener)
      {
        Type = type;
        Listener = listener;
      }
    }

    readonly Dictionary<Type, EventQuery<T>> _dictionary;
    readonly Dictionary<Type, EventQuery<T>> _genericDictionary;
    readonly List<EventQuery<T>> _orderedQueries;
    readonly List<EventQuery<T>> _orderedGenericQueries;
    readonly OrderedListenerSet<T> _listeners;
    PendingOperation[] _pendingOperations;
    int _pendingOperationCount;
    int _raiseDepth;

    public EventQuery()
    {
      _dictionary = new Dictionary<Type, EventQuery<T>>();
      _genericDictionary = new Dictionary<Type, EventQuery<T>>();
      _orderedQueries = new List<EventQuery<T>>();
      _orderedGenericQueries = new List<EventQuery<T>>();
      _listeners = new OrderedListenerSet<T>(InitialCapacity);
      _pendingOperations = new PendingOperation[InitialCapacity];
    }

    /// <summary>
    /// Registers a listener once on this query. Listeners are invoked in registration
    /// order. Registering the same delegate again has no effect; unsubscribing and then
    /// registering it again appends it to the end. Mutations requested while this query
    /// is dispatching are applied after its outermost dispatch completes.
    /// </summary>
    public virtual IIListener Listen(ListenerDelegate<T> @event)
    {
      if (_raiseDepth > 0)
        EnqueueOperation(PendingOperationType.Subscribe, @event);
      else
        _listeners.Add(@event);

      return new Listener(() => UnsubscribeInternal(@event));
    }

    /// <summary>
    /// Dispatches an event through this query. The outermost query dispatch resets
    /// propagation and assigns a new RaiseUniqueId. Nested query dispatch preserves both.
    /// Direct listeners run in registration order, followed by IParameter filter branches
    /// and then class filter branches, each in first-definition order.
    /// </summary>
    public virtual void Raise(T @event)
    {
      var isDepthZero = @event.ActiveRaiseDepth == 0;
      @event.ActiveRaiseDepth++;
      if (isDepthZero)
      {
        @event.ResetPropagation();
        @event.RaiseUniqueId = EventBus.NextRaiseUniqueId();
      }

      _raiseDepth++;
      try
      {
        if (!_listeners.Raise(@event)) return;

        var queryCount = _orderedQueries.Count;
        for (var i = 0; i < queryCount; i++)
        {
          _orderedQueries[i].Raise(@event);
          if (@event.IsPropagationStopped)
          {
            return;
          }
        }

        var genericQueryCount = _orderedGenericQueries.Count;
        for (var i = 0; i < genericQueryCount; i++)
        {
          _orderedGenericQueries[i].Raise(@event);
          if (@event.IsPropagationStopped)
          {
            return;
          }
        }
      }
      finally
      {
        _raiseDepth--;
        @event.ActiveRaiseDepth--;
        if (_raiseDepth == 0) ProcessPendingOperations();
      }
    }

    /// <summary>
    /// Gets the query for an IParameter value. Parameter-type branches are dispatched
    /// deterministically in the order in which each type was first defined.
    /// </summary>
    public EventQuery<T> Where<TParameterType>(in object value) where TParameterType : IParameter
    {
      var parameterType = typeof(TParameterType);
      ParameterQuery<T, TParameterType> pq;
      if (!_dictionary.ContainsKey(parameterType))
      {
        pq = new ParameterQuery<T, TParameterType>();
        _dictionary[parameterType] = pq;
        _orderedQueries.Add(pq);
        return pq.Where(value);
      }

      pq = (ParameterQuery<T, TParameterType>)_dictionary[parameterType];
      return pq.Where(value);
    }

    /// <summary>
    /// Gets the query for a class parameter value. Class-parameter branches are dispatched
    /// deterministically in the order in which each type was first defined.
    /// </summary>
    public EventQuery<T> Where<TParameterType>(in TParameterType value) where TParameterType : class
    {
      var parameterType = typeof(TParameterType);
      GenericParameterQuery<T, TParameterType> pq;
      if (!_genericDictionary.ContainsKey(parameterType))
      {
        pq = new GenericParameterQuery<T, TParameterType>();
        _genericDictionary[parameterType] = pq;
        _orderedGenericQueries.Add(pq);
        return pq.Where(value);
      }

      pq = (GenericParameterQuery<T, TParameterType>)_genericDictionary[parameterType];
      return pq.Where(value);
    }

    void ProcessPendingOperations()
    {
      var operationCount = _pendingOperationCount;
      _pendingOperationCount = 0;
      for (var i = 0; i < operationCount; i++)
      {
        var operation = _pendingOperations[i];
        _pendingOperations[i] = default(PendingOperation);
        if (operation.Type == PendingOperationType.Subscribe)
          _listeners.Add(operation.Listener);
        else
          _listeners.Remove(operation.Listener);
      }
    }

    void EnqueueOperation(PendingOperationType type, ListenerDelegate<T> listener)
    {
      if (_pendingOperationCount == _pendingOperations.Length)
        Array.Resize(ref _pendingOperations, _pendingOperations.Length * 2);

      _pendingOperations[_pendingOperationCount++] = new PendingOperation(type, listener);
    }

    void UnsubscribeInternal(ListenerDelegate<T> @event)
    {
      if (_raiseDepth > 0)
        EnqueueOperation(PendingOperationType.Unsubscribe, @event);
      else
        _listeners.Remove(@event);
    }
  }

  public class ParameterQuery<T, TParameterType> : EventQuery<T>
    where T : BusEvent where TParameterType : IParameter
  {
    readonly Dictionary<object, EventQuery<T>> _valueDictionary;

    public ParameterQuery()
    {
      _valueDictionary = new Dictionary<object, EventQuery<T>>();
    }

    public override void Raise(T @event)
    {
      var isDepthZero = @event.ActiveRaiseDepth == 0;
      @event.ActiveRaiseDepth++;
      if (isDepthZero)
      {
        @event.ResetPropagation();
        @event.RaiseUniqueId = EventBus.NextRaiseUniqueId();
      }

      try
      {
        var type = typeof(TParameterType);
        var value = @event.Get<TParameterType>();
        EventQuery<T> eventQuery;
        if (value != null && _valueDictionary.TryGetValue(value, out eventQuery))
          eventQuery.Raise(@event);
      }
      finally
      {
        @event.ActiveRaiseDepth--;
      }
    }

    public EventQuery<T> Where(in object value)
    {
      if (!_valueDictionary.ContainsKey(value))
      {
        var eq = new EventQuery<T>();
        _valueDictionary[value] = eq;
      }

      return _valueDictionary[value];
    }
  }

  public class GenericParameterQuery<T, TParameterType> : EventQuery<T>
    where T : BusEvent where TParameterType : class
  {
    readonly Dictionary<object, EventQuery<T>> _valueDictionary;

    public GenericParameterQuery()
    {
      _valueDictionary = new Dictionary<object, EventQuery<T>>();
    }

    public override void Raise(T @event)
    {
      var isDepthZero = @event.ActiveRaiseDepth == 0;
      @event.ActiveRaiseDepth++;
      if (isDepthZero)
      {
        @event.ResetPropagation();
        @event.RaiseUniqueId = EventBus.NextRaiseUniqueId();
      }

      try
      {
        var type = typeof(TParameterType);
        var value = @event.GetGeneric<TParameterType>();
        EventQuery<T> eventQuery;
        if (value != null && _valueDictionary.TryGetValue(value, out eventQuery))
          eventQuery.Raise(@event);
      }
      finally
      {
        @event.ActiveRaiseDepth--;
      }
    }

    public EventQuery<T> Where(in object value)
    {
      if (!_valueDictionary.ContainsKey(value))
      {
        var eq = new EventQuery<T>();
        _valueDictionary[value] = eq;
      }

      return _valueDictionary[value];
    }
  }
}
