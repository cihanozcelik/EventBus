# Nopnag StateMachineLib (TypeScript)

A lightweight and flexible state machine library for TypeScript and JavaScript, designed for managing game logic flow.

## 🚀 Getting Started

After installing this package, you can start using the library immediately.

## Overview

StateMachineLib provides tools to structure application logic into distinct states and define transitions between them. It allows for multiple state graphs running concurrently, supports hierarchical state machines (subgraphs), dynamic graph attachment/detachment, and a local event system alongside global event integration.

## API Usage Quick Reference

```typescript
import { StateMachine, StateGraph, StateUnit } from 'nopnag-statemachine';
import { MyGameEvent, MySubEvent } from './events'; // Assuming events are defined

// --- Core Setup & Lifecycle ---
const stateMachine = new StateMachine();
const mainGraph = stateMachine.createGraph(); // Creates a graph hosted by the StateMachine
const state1 = mainGraph.createState(); // Preferred: Creates a state. Name is managed by user if needed.
mainGraph.initialUnit = state1; // Or first created state is initial by default.

// Activate/Deactivate a graph
// Graphs created via stateMachine.createGraph() or stateUnit.createGraph() are turned on by default.
mainGraph.setTurnedOn(false); // Explicitly deactivate a graph
mainGraph.setTurnedOn(true);  // Reactivate it

stateMachine.start(); // Enters initial state of all 'turned on' and 'powered' graphs.
// Call in game loop (e.g., Unity's Update or JS requestAnimationFrame)
// function update(deltaTime) {
    stateMachine.updateMachine(deltaTime);       // Standard Update
    // stateMachine.fixedUpdateMachine(deltaTime);  // Physics Update
    // stateMachine.lateUpdateMachine(deltaTime);   // Late Update
// }

stateMachine.exit(); // Exits all graphs and their current states. Power state is also turned off.
// Important: Call when StateMachine is no longer needed if not using a wrapper.
stateMachine.dispose(); 

// --- Hierarchical Graphs (Subgraphs) ---
// StateUnits can host subgraphs because StateUnit implements IGraphHost
const parentStateHostingSubgraph = mainGraph.createState();
const subGraph = parentStateHostingSubgraph.createGraph(); // Create a new subgraph under StateUnit
const childStateInSubgraph = subGraph.createState();
subGraph.initialUnit = childStateInSubgraph;
// Subgraphs can also host their own subgraphs, creating deeper hierarchies.
const subSubGraph = childStateInSubgraph.createGraph();
// ...

// --- Dynamic Graph Management (Attach/Detach) ---
// Create a graph independently
const independentGraph = new StateGraph();
const someIndependentState = independentGraph.createState();
independentGraph.initialUnit = someIndependentState;
// ... configure independentGraph ...

// Attach it to the StateMachine or a StateUnit
stateMachine.attachGraph(independentGraph); // Becomes a top-level graph
// parentStateHostingSubgraph.attachGraph(anotherIndependentGraph); // Attaches under the parent state

// Detach a graph (preserves its current state, makes it inactive, disconnects power)
stateMachine.detachGraph(independentGraph);
// parentStateHostingSubgraph.detachGraph(subGraph);

// --- Local Event System ---
// StateMachine and StateUnit (if hosting graphs) act as IGraphHost and have a LocalEventBus
stateMachine.localRaise(new MyGameEvent()); // Event propagates to graphs hosted by stateMachine
parentStateHostingSubgraph.localRaise(new MySubEvent()); // Event propagates to subGraph

// --- StateUnit Logic Actions (OnEnter, OnUpdate, OnExit, etc.) ---
state1.onEnter = () => { /* Logic for state1 Enter */ };
state1.onUpdate = (timeInState) => { /* Logic for state1 Update */ };
// ... (similarly for OnExit, OnFixedUpdate, OnLateUpdate)

// --- Time-Based Callbacks (within StateUnit) ---
state1.at(2.0, () => { /* Action after 2 seconds in state1 */ });
state1.atEvery(1.0, () => { /* Action every 1 second in state1 */ });

// --- Event Listening (within StateUnit, for EventBus events) ---
// state.on(MyEvent) listens to both global EventBus and relevant LocalEventBus events
state1.on(MyGameEvent).listen((evt) => { /* Handle MyGameEvent */ });

// --- Fluent Transition Creation API (Examples) ---
// Unlike C# operator overloading (>), TypeScript uses .to()
state1.to(childStateInSubgraph).when((elapsedTime) => elapsedTime > 1.0);
childStateInSubgraph.to(state1).on(MyGameEvent);
StateGraph.any.to(state1).immediately(); // Any-state transition within a graph
```

## Defining Transitions (Fluent API)

Transitions are defined using a fluent syntax.

### `(fromState).to(toState).after(delay)`:

This defines a `TransitionAfter` that triggers automatically after the specified delay (in seconds).

```typescript
// Transition from waitingState to actionState after 2.5 seconds
waitingState.to(actionState).after(2.5);
```

### `(fromState).to(toState).on(EventType, predicate?)`:

This defines a `TransitionByEvent` that triggers when a specific `BusEvent` is raised on the associated EventBus. It supports an optional predicate function for filtering.

```typescript
// Transition when 'KeyCardCollectedEvent' is raised.
lockedDoorState.to(unlockedDoorState).on(KeyCardCollectedEvent, 
    // Filter by the string value "KeyCard"
    evt => evt.keyType === "KeyCard"
);

// Optional additional predicate on the event object
// evt => evt.collector.isPlayer 
```

### `(fromState).to(toState).onSignal(signal) (for Actions/Signals)`:

This defines a `TransitionByAction` that triggers when the provided signal is invoked. (The C# version uses `ref Action`, here we use a `Signal` object).

```typescript
import { Signal } from 'nopnag-statemachine';

const playerJumped = new Signal();
const playerScoredPoints = new Signal<number>();

// ... in setup ...
groundedState.to(jumpingState).onSignal(playerJumped);
anyState.to(scoreCelebrationState).onSignal(playerScoredPoints);

// ... elsewhere ...
playerJumped.dispatch();
playerScoredPoints.dispatch(100);
```

### `(fromState).to(toState).immediately()`:

This defines a `DirectTransition` that occurs unconditionally as soon as the source state is entered or updated, causing an immediate transition to the target state. It's useful for states that are purely transitional or serve as entry points that should immediately redirect.

```typescript
const entryPointState = myGraph.createState();
const actualStartState = myGraph.createState();

// From entryPointState, immediately go to actualStartState
entryPointState.to(actualStartState).immediately();
```

### `(fromState).to(targetStates).when(indexPredicate)`:

You can define transitions from a single state to one of several possible target states based on an index returned by a condition function. This is useful for decision points where the next state depends on dynamic criteria.

The `when` method, when used with an array of target `StateUnit`s, expects its predicate to return an integer.

* If the integer is a valid index into the array of target states (0 to N-1), a transition to the state at that index occurs.
* If the integer is -1 (or any out-of-bounds negative number), no transition occurs.

```typescript
const decisionState = myGraph.createState();
const optionAState = myGraph.createState();
const optionBState = myGraph.createState();
const optionCState = myGraph.createState();

// From decisionState, transition to one of [optionAState, ...] based on index
decisionState.to([optionAState, optionBState, optionCState]).when(elapsedTime => {
    // Assuming 'player' and 'PlayerChoices' are defined elsewhere
    // and 'elapsedTime' is the time since 'decisionState' became active.
    if (player.choice === PlayerChoices.A) return 0;       // Transition to optionAState
    if (player.choice === PlayerChoices.B) return 1;       // Transition to optionBState
    if (elapsedTime > 10.0 && player.isIdle) return 2;     // Transition to optionCState
    return -1;                                             // No transition
});
```

### `(fromState).toDynamic().when(dynamicTargetPredicate)`:

This defines a `ConditionalTransition` where the target state is determined at runtime by the `dynamicTargetPredicate`.

You initiate this by calling `.toDynamic()` (equivalent to `> StateGraph.DynamicTarget`). The subsequent `.when()` method then takes a predicate of type `(elapsedTime: number) => StateUnit | null`.

* **`dynamicTargetPredicate`**: A function that receives the elapsed time in the source state and should return:  
   * A non-null `StateUnit` to transition to that state.  
   * `null` to indicate that no transition should occur at this time.

```typescript
const patrollingState = myGraph.createState();
const chasingState = myGraph.createState();
const investigatingState = myGraph.createState();

// From patrollingState, transition to a dynamically chosen state
patrollingState.toDynamic().when(elapsedTime => {
    if (canSeePlayer()) return chasingState;
    if (heardNoise()) return investigatingState;
    return null; // Stay in patrolling state
});
```

Future methods (like for conditional transitions to a dynamically chosen single state) will be added to this fluent API.

## Practical Usage Example (Character Controller)

This example demonstrates a character controller with Idle, Moving, Jumping, and Stunned states, using various transitions.

```typescript
import { StateMachine } from 'nopnag-statemachine';
import { BusEvent } from 'nopnag-eventbus';

// --- Define Events used for Transitions (if not already globally defined) ---
class DamageTakenEvent extends BusEvent { } 
// class JumpInputEvent extends BusEvent { } // Example if using event for jump

class CharacterController {
    private stateMachine: StateMachine;
    // Mock Unity Components
    private rb = { addForce: (vec: any) => {} }; 
    private transform = { position: { x: 0, y: 0, z: 0 } };
    private jumpForce = 5;
    private stunDuration = 0.5; 

    constructor() {
        // Create a lifecycle-managed StateMachine (Manual management in TS example)
        this.stateMachine = new StateMachine();
        this.setupStateMachine();
    }

    setupStateMachine() {
        // --- Initialize States and Transitions --- 
        const movementGraph = this.stateMachine.createGraph();

        // Define States
        const idleState = movementGraph.createState();
        const movingState = movementGraph.createState();
        const jumpingState = movementGraph.createState();
        const stunnedState = movementGraph.createState();

        // Assign State Logic
        idleState.onEnter = () => { 
            console.log("Entering Idle State"); 
        };
        // idleState.onUpdate = (timeInState) => { /* Maybe play idle animation. */ };
        
        movingState.onEnter = () => console.log("Entering Moving State");
        movingState.onUpdate = (timeInState) => 
        { 
            const moveDir = this.getMovementInput(); 
            // rb.AddForce(moveDir * 10f * deltaTime); // Needs explicit deltaTime in TS
        };

        jumpingState.onEnter = () => 
        {
            console.log("Entering Jumping State");
            // rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
        };
        
        stunnedState.onEnter = () => console.log("Entering Stunned State");
        // stunnedState.onUpdate = (timeInState) => { /* Maybe play stunned animation. */ };

        // Define Transitions (using Fluent API)
        idleState.to(stunnedState).on(DamageTakenEvent);
        movingState.to(stunnedState).on(DamageTakenEvent);
        jumpingState.to(stunnedState).on(DamageTakenEvent);
        
        stunnedState.to(idleState).after(this.stunDuration);
        
        idleState.to(movingState).when(elapsedTime => this.getMovementInputMagnitude() > 0.1);
        movingState.to(idleState).when(elapsedTime => this.getMovementInputMagnitude() <= 0.1);
        
        // Assuming Input.GetButtonDown("Jump") equivalent
        idleState.to(jumpingState).when(elapsedTime => this.isJumpPressed());
        movingState.to(jumpingState).when(elapsedTime => this.isJumpPressed());
        
        jumpingState.to(idleState).after(1.0); 

        // Set Initial State
        movementGraph.initialUnit = idleState;
        
        // Start the machine
        this.stateMachine.start();
    }
    
    // Mock Input methods
    getMovementInput() { return { x: 1, y: 0, z: 0 }; }
    getMovementInputMagnitude() { return 0; }
    isJumpPressed() { return false; }

    // Call this in game loop
    update(deltaTime: number) {
        this.stateMachine.updateMachine(deltaTime);
    }
}
```

## Installation

**Important:** This package depends on `nopnag-eventbus`. You need to install both packages for StateMachineLib to work correctly.

```bash
npm install nopnag-eventbus nopnag-statemachine
```

Or add to `package.json`:

```json
{
  "dependencies": {
    "nopnag-eventbus": "^1.1.0",
    "nopnag-statemachine": "^1.0.0"
  }
}
```
