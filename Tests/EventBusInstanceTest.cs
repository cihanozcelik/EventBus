using NUnit.Framework;

namespace Nopnag.EventBusLib.Tests
{
  public class EventBusInstanceTest
  {
    public class Source : object, IParameter
    {
    }

    public class Target : object, IParameter
    {
    }

    public class TestEvent : BusEvent
    {
      public string Message { get; set; }
    }

    public sealed class UnobservedEvent : BusEvent
    {
    }

    [Test]
    public void BackwardCompatibility_StaticAPIStillWorks()
    {
      var eventReceived = false;
      var sourceObject  = new object(); // Use same instance

      // Use old static API
      var listener = EventBus<TestEvent>.Where<Source>(sourceObject)
        .Listen(e => eventReceived = true);

      var testEvent = new TestEvent();
      testEvent.Set<Source>(sourceObject); // Use same instance
      EventBus.Raise(testEvent);
      Assert.IsTrue(eventReceived);
      listener.Unsubscribe();
    }

    [Test]
    public void LocalEventBus_BasicUsage()
    {
      var    localBus        = new LocalEventBus();
      var    eventReceived   = false;
      string receivedMessage = null;

      // Subscribe using instance API
      var listener = localBus.On<TestEvent>().Listen(e =>
      {
        eventReceived   = true;
        receivedMessage = e.Message;
      });

      // Raise event using instance API
      var testEvent = new TestEvent { Message = "Hello from instance!" };
      localBus.Raise(testEvent);

      Assert.IsTrue(eventReceived);
      Assert.AreEqual("Hello from instance!", receivedMessage);

      listener.Unsubscribe();
    }

    [Test]
    public void LocalEventBus_WithParameters()
    {
      var localBus = new LocalEventBus();
      var source1  = new object();
      var source2  = new object();
      var target1  = new object();

      var source1EventReceived = false;
      var source2EventReceived = false;

      // Subscribe with parameter filtering using Where
      var listener1 = localBus.On<TestEvent>()
        .Where<Source>(source1)
        .Where<Target>(target1)
        .Listen(e => source1EventReceived = true);

      var listener2 = localBus.On<TestEvent>()
        .Where<Source>(source2)
        .Listen(e => source2EventReceived = true);

      // Raise event that should only trigger listener1
      var testEvent = new TestEvent();
      testEvent.Set<Source>(source1);
      testEvent.Set<Target>(target1);
      localBus.Raise(testEvent);

      Assert.IsTrue(source1EventReceived);
      Assert.IsFalse(source2EventReceived);

      listener1.Unsubscribe();
      listener2.Unsubscribe();
    }

    [Test]
    public void MultipleLocalEventBus_AreIsolated()
    {
      var bus1 = new LocalEventBus();
      var bus2 = new LocalEventBus();

      var bus1EventReceived = false;
      var bus2EventReceived = false;

      var listener1 = bus1.On<TestEvent>().Listen(e => bus1EventReceived = true);
      var listener2 = bus2.On<TestEvent>().Listen(e => bus2EventReceived = true);

      // Raise on bus1 - should not affect bus2
      bus1.Raise(new TestEvent());
      Assert.IsTrue(bus1EventReceived);
      Assert.IsFalse(bus2EventReceived);

      // Reset and test the other way
      bus1EventReceived = false;
      bus2EventReceived = false;

      bus2.Raise(new TestEvent());
      Assert.IsFalse(bus1EventReceived);
      Assert.IsTrue(bus2EventReceived);

      listener1.Unsubscribe();
      listener2.Unsubscribe();
    }

    [Test]
    public void StaticAndLocalEventBus_AreIsolated()
    {
      var localBus              = new LocalEventBus();
      var staticEventReceived   = false;
      var instanceEventReceived = false;

      // Subscribe to static EventBus
      var staticListener = EventBus<TestEvent>.Listen(e => staticEventReceived = true);

      // Subscribe to local EventBus
      var instanceListener = localBus.On<TestEvent>().Listen(e => instanceEventReceived = true);

      // Raise event on local bus - should not affect static
      localBus.Raise(new TestEvent { Message = "Local event" });
      Assert.IsFalse(staticEventReceived);
      Assert.IsTrue(instanceEventReceived);

      // Reset flags
      staticEventReceived   = false;
      instanceEventReceived = false;

      // Raise event on static - should not affect local
      EventBus.Raise(new TestEvent { Message = "Static event" });
      Assert.IsTrue(staticEventReceived);
      Assert.IsFalse(instanceEventReceived);

      staticListener.Unsubscribe();
      instanceListener.Unsubscribe();
    }

    // -------------------- New tests for RaiseUniqueId and ActiveRaiseDepth (instance/static) --------------------

    [Test]
    public void LocalEventBus_AssignsRaiseUniqueId_PerRaise()
    {
      var localBus = new LocalEventBus();
      long id1     = 0;
      long id2     = 0;

      var listener = localBus.On<TestEvent>().Listen(e =>
      {
        if (id1 == 0) id1 = e.RaiseUniqueId; else id2 = e.RaiseUniqueId;
      });

      var e = new TestEvent();
      localBus.Raise(e);
      localBus.Raise(e);

      Assert.Greater(id1, 0);
      Assert.Greater(id2, 0);
      Assert.AreNotEqual(id1, id2);

      listener.Unsubscribe();
    }

    [Test]
    public void LocalEventBus_FirstRaiseWithoutQuery_DoesNotAllocate()
    {
      var warmupBus = new LocalEventBus();
      var warmupEvent = new UnobservedEvent();
      warmupBus.Raise(warmupEvent);

      var localBus = new LocalEventBus();
      var unobservedEvent = new UnobservedEvent();
      System.GC.GetAllocatedBytesForCurrentThread();
      var allocatedBefore = System.GC.GetAllocatedBytesForCurrentThread();

      localBus.Raise(unobservedEvent);

      var allocatedBytes = System.GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
      Assert.AreEqual(0, allocatedBytes,
        "An unobserved local event must not create query topology during Raise.");
    }

    [Test]
    public void LocalEventBus_UnobservedRaise_PreservesDispatchLifecycle()
    {
      var localBus = new LocalEventBus();
      var unobservedEvent = new UnobservedEvent();
      unobservedEvent.StopPropagation();

      localBus.Raise(unobservedEvent);

      var firstRaiseId = unobservedEvent.RaiseUniqueId;
      Assert.Greater(firstRaiseId, 0);
      Assert.IsFalse(unobservedEvent.IsPropagationStopped);

      unobservedEvent.StopPropagation();
      localBus.Raise(unobservedEvent);

      Assert.AreNotEqual(firstRaiseId, unobservedEvent.RaiseUniqueId);
      Assert.IsFalse(unobservedEvent.IsPropagationStopped);
    }

    [Test]
    public void LocalEventBus_ChainedFilters_PreserveSameId()
    {
      var localBus = new LocalEventBus();
      var src      = new object();
      var tgt      = new object();

      long idUnfiltered = 0;
      long idFiltered   = 0;

      var l1 = localBus.On<TestEvent>().Listen(e => { idUnfiltered = e.RaiseUniqueId; });
      var l2 = localBus.On<TestEvent>().Where<Source>(src).Where<Target>(tgt).Listen(e => { idFiltered = e.RaiseUniqueId; });

      var e = new TestEvent();
      e.Set<Source>(src);
      e.Set<Target>(tgt);
      localBus.Raise(e);

      Assert.Greater(idUnfiltered, 0);
      Assert.AreEqual(idUnfiltered, idFiltered);

      l1.Unsubscribe();
      l2.Unsubscribe();
    }

    [Test]
    public void StaticGenericVsStaticWrapper_BothAssignIds()
    {
      long idFromGeneric = 0;
      long idFromWrapper = 0;

      var l = EventBus<TestEvent>.Listen(e => { idFromGeneric = e.RaiseUniqueId; });
      var e = new TestEvent();
      EventBus<TestEvent>.Raise(e);
      Assert.Greater(idFromGeneric, 0);
      l.Unsubscribe();

      var l2 = EventBus<TestEvent>.Listen(e => { idFromWrapper = e.RaiseUniqueId; });
      var e2 = new TestEvent();
      EventBus.Raise(e2);
      Assert.Greater(idFromWrapper, 0);
      l2.Unsubscribe();
    }
  }
}
