### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
SIA001 | Sia.Systems | Error | Type marked with [SiaSystem] but does not implement ISystem
SIA100 | Sia.Reactive | Error | Reactive component container must be static and partial
SIA101 | Sia.Reactive | Error | Reactive component must define a valid InitialState/Reduce/Render member
SIA102 | Sia.Reactive | Error | Reactive component data model must use immutable records
SIA103 | Sia.Reactive | Error | Nested Reactive callbacks must be static
SIA104 | Sia.Reactive | Error | Pure Reactive functions cannot access ambient Sia.Context state
SIA105 | Sia.Reactive | Error | Reactive event messages must be assignable to the component reducer message type
