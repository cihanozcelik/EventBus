using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Nopnag.EventBusLib.Tests
{
  public class DeterministicDispatchTests
  {
    sealed class OrderedEvent : BusEvent
    {
    }

    sealed class FirstParameter : IParameter
    {
    }

    sealed class SecondParameter : IParameter
    {
    }

    [Test]
    public void ListenersRunInSubscriptionOrderOnEveryRaise()
    {
      var query = new EventQuery<OrderedEvent>();
      var order = new List<int>();

      query.Listen(_ => order.Add(1));
      query.Listen(_ => order.Add(2));
      query.Listen(_ => order.Add(3));

      var @event = new OrderedEvent();
      for (var raiseIndex = 0; raiseIndex < 32; raiseIndex++)
      {
        order.Clear();
        query.Raise(@event);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, order);
      }
    }

    [Test]
    public void DuplicateDelegateIsRegisteredOnlyOnce()
    {
      var query = new EventQuery<OrderedEvent>();
      var callCount = 0;
      ListenerDelegate<OrderedEvent> listener = _ => callCount++;

      query.Listen(listener);
      query.Listen(listener);
      query.Raise(new OrderedEvent());

      Assert.AreEqual(1, callCount);
    }

    [Test]
    public void ResubscribedListenerMovesToEndOfOrder()
    {
      var query = new EventQuery<OrderedEvent>();
      var order = new List<int>();
      ListenerDelegate<OrderedEvent> first = _ => order.Add(1);
      ListenerDelegate<OrderedEvent> second = _ => order.Add(2);

      var firstHandle = query.Listen(first);
      query.Listen(second);
      firstHandle.Unsubscribe();
      query.Listen(first);

      query.Raise(new OrderedEvent());

      CollectionAssert.AreEqual(new[] { 2, 1 }, order);
    }

    [Test]
    public void OrderSurvivesResizeRemovalAndFreeNodeReuse()
    {
      var query = new EventQuery<OrderedEvent>();
      var order = new List<int>();
      var handles = new List<IIListener>();

      for (var i = 0; i < 12; i++)
      {
        var listenerIndex = i;
        handles.Add(query.Listen(_ => order.Add(listenerIndex)));
      }

      query.Raise(new OrderedEvent());
      CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }, order);

      handles[3].Unsubscribe();
      handles[7].Unsubscribe();
      query.Listen(_ => order.Add(12));
      query.Listen(_ => order.Add(13));

      order.Clear();
      query.Raise(new OrderedEvent());
      CollectionAssert.AreEqual(new[] { 0, 1, 2, 4, 5, 6, 8, 9, 10, 11, 12, 13 }, order);
    }

    [Test]
    public void SubscriptionDuringRaiseStartsOnNextRaiseAtEndOfOrder()
    {
      var query = new EventQuery<OrderedEvent>();
      var order = new List<int>();
      IIListener lateHandle = null;

      query.Listen(_ =>
      {
        order.Add(1);
        if (lateHandle == null)
          lateHandle = query.Listen(__ => order.Add(3));
      });
      query.Listen(_ => order.Add(2));

      var @event = new OrderedEvent();
      query.Raise(@event);
      CollectionAssert.AreEqual(new[] { 1, 2 }, order);

      order.Clear();
      query.Raise(@event);
      CollectionAssert.AreEqual(new[] { 1, 2, 3 }, order);
    }

    [Test]
    public void FilterBranchesRunInFirstDefinitionOrder()
    {
      var query = new EventQuery<OrderedEvent>();
      var order = new List<int>();
      var firstValue = new object();
      var secondValue = new object();

      query.Where<SecondParameter>(secondValue).Listen(_ => order.Add(2));
      query.Where<FirstParameter>(firstValue).Listen(_ => order.Add(1));

      var @event = new OrderedEvent();
      @event.Set<FirstParameter>(firstValue);
      @event.Set<SecondParameter>(secondValue);
      query.Raise(@event);

      CollectionAssert.AreEqual(new[] { 2, 1 }, order);
    }

    [Test]
    public void ExceptionDoesNotPoisonDispatchOrLoseDeferredOperations()
    {
      var query = new EventQuery<OrderedEvent>();
      var recoveredListenerCalls = 0;
      IIListener recoveredHandle = null;

      var throwingHandle = query.Listen(_ =>
      {
        recoveredHandle = query.Listen(__ => recoveredListenerCalls++);
        throw new InvalidOperationException("Expected test exception");
      });

      Assert.Throws<InvalidOperationException>(() => query.Raise(new OrderedEvent()));

      throwingHandle.Unsubscribe();
      query.Raise(new OrderedEvent());

      Assert.AreEqual(1, recoveredListenerCalls);
      recoveredHandle.Unsubscribe();
    }

    [Test]
    public void RecursiveRaiseDoesNotApplyDeferredSubscriptionTooEarly()
    {
      var query = new EventQuery<OrderedEvent>();
      var lateListenerCalls = 0;
      var isNestedRaise = false;
      IIListener lateHandle = null;

      query.Listen(@event =>
      {
        if (isNestedRaise) return;

        isNestedRaise = true;
        lateHandle = query.Listen(_ => lateListenerCalls++);
        query.Raise(@event);
        isNestedRaise = false;
      });

      var @event = new OrderedEvent();
      query.Raise(@event);
      Assert.AreEqual(0, lateListenerCalls);

      isNestedRaise = true;
      query.Raise(@event);
      Assert.AreEqual(1, lateListenerCalls);
      lateHandle.Unsubscribe();
    }

    [Test]
    public void RepeatedRaiseDoesNotAllocateAfterInitialization()
    {
      var query = new EventQuery<OrderedEvent>();
      var @event = new OrderedEvent();
      var callCount = 0;
      var filterValue = new object();
      query.Listen(_ => callCount++);
      query.Where<FirstParameter>(filterValue).Listen(_ => callCount++);
      @event.Set<FirstParameter>(filterValue);

      query.Raise(@event);
      GC.GetAllocatedBytesForCurrentThread();
      var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

      for (var i = 0; i < 1000; i++)
        query.Raise(@event);

      var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
      Assert.AreEqual(0, allocatedBytes,
        "A warmed-up Raise path must not allocate managed memory.");
      Assert.AreEqual(2002, callCount);
    }
  }
}
