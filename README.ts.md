# Nopnag EventBus (TypeScript)

A flexible and queryable event bus system for TypeScript and JavaScript.

## Overview

This EventBus implementation provides a straightforward way to decouple different parts of your application by using a publish-subscribe pattern. Instead of direct method calls, systems can raise events, and other systems can listen for specific events they are interested in, optionally filtering them based on event parameters.

The library supports both **static (global)** and **instance-based (local)** EventBus usage, giving you flexibility in how you structure your event communication.

## Key Features

*   **Type-Safe Events:** Uses Classes and Generics to ensure compile-time safety for event payloads.
*   **Parameter Querying:** Allows listeners to subscribe only to events that match specific parameter values or types. Parameter keys are Class Constructors (tokens) that map to specific value types.
*   **Efficient Filtering:** Filtering logic is handled *internally* by the EventBus before invoking listeners. When an event is raised, the system efficiently finds only the relevant subscribers based on their `where` clauses, leading to significantly better performance compared to manual checking in every listener.
*   **Decoupled Architecture:** Promotes cleaner code by reducing direct dependencies between components.
*   **Easy Unsubscription:** Returns a standardized unsubscribe function (or handle) for easy removal of subscriptions.
*   **Dual API Support:** Both static access via `EventBus` class and instance-based usage via `new LocalEventBus()`.
*   **Event Isolation:** Instance-based LocalEventBuses are completely isolated from each other and from the global static EventBus.

## API Overview

### Static API (Global EventBus)
```typescript
// Traditional static API - global event system
EventBus.on(MyEvent).listen(handler);
EventBus.on(MyEvent).where(ParameterType, value).listen(handler);
EventBus.raise(myEventInstance);
```

### Instance API (Local EventBus)
```typescript
// New instance-based API - create isolated LocalEventBus instances
const localBus = new LocalEventBus();
localBus.on(MyEvent).listen(handler);
localBus.on(MyEvent).where(ParameterType, value).where(OtherParam, otherValue).listen(handler);
localBus.raise(myEventInstance);
```

## How It Works

1.  **Define Events:** Create classes that inherit from `BusEvent`.
2.  **Define Parameters (Optional, for Filtering):** If you want to filter events based on parameters, define classes to act as unique keys (Tokens). These classes help TypeScript infer the type of the value.
3.  **Set Parameters (Optional):** Use `eventInstance.set(ParameterClass, value)` to attach filterable parameters to an event instance. The `value` is the actual data (object, primitive, enum, etc.) you want to filter by.
4.  **Choose Your API:** Use either the static API for global events or create instance-based `LocalEventBus` for local/scoped events.
5.  **Raise Events:** Use `EventBus.raise(yourEventInstance)` (static) or `localBus.raise(yourEventInstance)` (instance) to publish an event.
6.  **Listen to Events:** Use `EventBus.on(YourEventType).listen(handler)` (static) or `localBus.on(YourEventType).listen(handler)` (instance) to subscribe to all events of a specific type.
7.  **Filtered Listening:** Use `.where(ParameterType, filterValue)` for both static and instance APIs to subscribe only to events where the parameter matches. Chain multiple conditions for more specific subscriptions.
8.  **Access Parameters:** Inside your listener handler, use `eventInstance.get(ParameterType)` to retrieve the value of a parameter that was set via `set()`.
9.  **Unsubscribe:** Keep the returned function from `listen()` and call it when you no longer need to listen.

## Usage Examples

### 1. Define Event and Parameters

```typescript
import { BusEvent } from 'nopnag-eventbus';

// --- Define Parameter Keys (Classes act as Tokens) ---

// Represents the source entity
export class Source extends Warrior {}

// Represents the destination entity
export class Destination extends Warrior {}

// Represents the type of weapon used (wrapper for enum/value)
export class WeaponType { constructor(public value: Weapon) {} }

// Represents a numerical amount
export class Amount { constructor(public value: number) {} }

// --- Define an entity (used by parameters) ---
export class Warrior { 
    constructor(public name: string = "") {} 
}

export enum Weapon { Sword, Axe, Bow }

// --- Define the Event --- 
export class CombatEvent extends BusEvent {
  // Non-filterable data can still go here as standard class properties
  public logMessage: string = "";
}
```

### 2. Instance-Based LocalEventBus (Recommended for Local Scopes)

```typescript
import { LocalEventBus } from 'nopnag-eventbus';

export class CombatSystem {
    // Create a local EventBus for this combat system
    private readonly _combatBus = new LocalEventBus();
    
    constructor() {
        // Subscribe to combat events in this local scope
        this._combatBus.on(CombatEvent).listen(this.onAnyCombat);
        
        // Subscribe with parameter filtering using Where
        this._combatBus.on(CombatEvent)
            .where(WeaponType, { value: Weapon.Sword }) // Structural matching or instance matching
            .where(Amount, { value: 15 })
            .listen(this.onSwordAttackWith15Damage);
    }
    
    simulateCombat() {
        const warrior1 = new Warrior("Hero");
        const warrior2 = new Warrior("Monster");
        
        // Create and configure the event
        const combatEvent = new CombatEvent();
        combatEvent.logMessage = "Warrior 1 attacks Warrior 2 with Sword for 15 damage.";
        
        // Fluent setters
        combatEvent.set(Source, new Source("Hero"))
                   .set(Destination, new Destination("Monster"))
                   .set(WeaponType, { value: Weapon.Sword })
                   .set(Amount, { value: 15 });
        
        // Raise the event on the local bus
        this._combatBus.raise(combatEvent);
    }
    
    private onAnyCombat = (evt: CombatEvent) => {
        console.log(`Combat occurred: ${evt.logMessage}`);
    }
    
    private onSwordAttackWith15Damage = (evt: CombatEvent) => {
        console.log("Specific sword attack with 15 damage detected!");
    }
}
```

### 3. Static EventBus (Global Events)

```typescript
import { EventBus, UnsubscribeFunc } from 'nopnag-eventbus';

export class GlobalCombatLogger {
    private _unsubscribe: UnsubscribeFunc | null = null;

    enable() {
        // Subscribe to global combat events
        this._unsubscribe = EventBus.on(CombatEvent).listen(this.onCombat);
    }

    disable() {
        if (this._unsubscribe) {
            this._unsubscribe();
            this._unsubscribe = null;
        }
    }

    private onCombat = (evt: CombatEvent) => {
        console.log(`Global Combat Log: ${evt.logMessage}`);
        
        // Access parameters using get(ParameterType)
        const source = evt.get(Source);      // inferred as Source | undefined
        const damage = evt.get(Amount);      // inferred as Amount | undefined
        const weapon = evt.get(WeaponType);  // inferred as WeaponType | undefined
    }
}

// Somewhere else in your code - raise global events
export class GameManager {
    triggerGlobalCombat() {
        const combatEvent = new CombatEvent();
        combatEvent.set(WeaponType, { value: Weapon.Axe });
        
        // Raise on the global EventBus
        EventBus.raise(combatEvent);
    }
}
```

### 4. Multiple Isolated LocalEventBus Instances

```typescript
export class MultiPlayerCombat {
    private readonly _player1Bus = new LocalEventBus();
    private readonly _player2Bus = new LocalEventBus();
    
    init() {
        // Each player has their own isolated event system
        this._player1Bus.on(CombatEvent).listen(evt => console.log("Player 1 combat"));
        this._player2Bus.on(CombatEvent).listen(evt => console.log("Player 2 combat"));
        
        // Events on player1Bus won't affect player2Bus and vice versa
        this._player1Bus.raise(new CombatEvent()); // Only Player 1 listener called
        this._player2Bus.raise(new CombatEvent()); // Only Player 2 listener called
    }
}
```

### 5. Backward Compatibility / Chain Logic

```typescript
// Complex chaining works just like in the C# version
const unsubscribe = EventBus.on(CombatEvent)
    .where(Source, warrior1)
    .where(WeaponType, { value: Weapon.Sword })
    .listen(onWarriorSwordAttack);

EventBus.raise(combatEvent);
unsubscribe();
```

## API Comparison

| Feature | Static API | Instance API |
|---------|------------|--------------|
| **Scope** | Global | Local/Isolated |
| **Creation** | Automatic (Import) | `new LocalEventBus()` |
| **Subscribe** | `EventBus.on(T).listen()` | `bus.on(T).listen()` |
| **Filter** | `.where(Key, value)` | `.where(Key, value)` |
| **Raise** | `EventBus.raise(event)` | `bus.raise(event)` |
| **Use Case** | Global app events | Component-specific events |

## When to Use Which API

### Use Static API When:
- You need global, application-wide events
- You want simple, straightforward event communication
- You're working with singleton services
- You need a central message hub

### Use Instance API When:
- You need isolated event systems (e.g., per-user, per-room)
- You want to avoid global state
- You're building modular, testable components
- You need multiple independent event systems
- You want better control over event scope and lifetime

## Installation

You can install this package using npm (Node Package Manager):

```bash
npm install nopnag-eventbus
```

Or add it to your `package.json` dependencies manually:

```json
{
  "dependencies": {
    "nopnag-eventbus": "^1.1.0"
  }
}
```
