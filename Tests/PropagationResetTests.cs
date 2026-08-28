using NUnit.Framework;

namespace Nopnag.EventBusLib.Tests
{
  public class PropagationResetTests
  {
    sealed class FilterParameter : IParameter
    {
    }

    sealed class TestEvent : BusEvent
    {
    }

    [Test]
    public void Raise_ReusedStoppedEvent_ResetsPropagationBeforeNextDispatch()
    {
      var query           = new EventQuery<TestEvent>();
      var stopPropagation = true;
      var rootCalls       = 0;
      var filteredCalls   = 0;

      var rootListener = query.Listen(e =>
      {
        rootCalls++;
        if (stopPropagation) e.StopPropagation();
      });
      var filteredListener = query.Where<FilterParameter>(7).Listen(_ => filteredCalls++);
      var testEvent        = new TestEvent();
      testEvent.Set<FilterParameter>(7);

      query.Raise(testEvent);

      Assert.AreEqual(1, rootCalls);
      Assert.AreEqual(0, filteredCalls);
      Assert.IsTrue(testEvent.IsPropagationStopped);

      stopPropagation = false;
      query.Raise(testEvent);

      Assert.AreEqual(2, rootCalls);
      Assert.AreEqual(1, filteredCalls);
      Assert.IsFalse(testEvent.IsPropagationStopped);

      rootListener.Unsubscribe();
      filteredListener.Unsubscribe();
    }
  }
}
