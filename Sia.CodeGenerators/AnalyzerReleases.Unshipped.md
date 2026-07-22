### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
SIA001 | Sia.Systems | Error | Type marked with [SiaSystem] but does not implement ISystem
SIA100 | Sia.Reactive | Error | Reactive component container must be static and partial
SIA101 | Sia.Reactive | Error | Reactive component must define a valid Render method
SIA102 | Sia.Reactive | Error | Reactive component props must be an immutable record struct
SIA103 | Sia.Reactive | Error | Nested Reactive callbacks must be static
SIA104 | Sia.Reactive | Error | Reactive renderers cannot access ambient Sia.Context state
