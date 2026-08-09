namespace Sia_Examples.Notebook;

public enum CellPhase
{
    Idle,
    Compiling,
    CompileError,
    Compiled,
    Running,
    RanSuccess,
    RanError,
    Interrupted,
}
