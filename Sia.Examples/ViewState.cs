namespace Sia.Examples;

public abstract record ViewState
{
    public sealed record Menu(
        int SelectedExample = 0,
        int HoveredExample = -1,
        float ScrollOffset = 0f) : ViewState
    {
        public Menu WithSelection(int selected) => this with { SelectedExample = selected };
        public Menu WithHover(int hovered) => this with { HoveredExample = hovered };
        public Menu WithScroll(float scrollDelta) => this with
        {
            ScrollOffset = Math.Max(0, ScrollOffset + scrollDelta)
        };
    }

    public sealed record Output(
        int SelectedExample,
        string Content,
        float ScrollOffset = 0f) : ViewState
    {
        public Output WithContent(string newContent) => this with { Content = newContent };
        public Output WithScroll(float scrollDelta) => this with
        {
            ScrollOffset = Math.Max(0, ScrollOffset + scrollDelta)
        };
    }

    public static ViewState InitialMenu() => new Menu();

    public static ViewState MenuFromOutput(Output output) => new Menu(output.SelectedExample);

    public static ViewState OutputFromMenu(Menu menu, string content) => new Output(menu.SelectedExample, content);
}

public static class ViewStateExtensions
{
    public static bool IsMenu(this ViewState state) => state is ViewState.Menu;
    public static bool IsOutput(this ViewState state) => state is ViewState.Output;

    public static ViewState.Menu AsMenu(this ViewState state) => (ViewState.Menu)state;
    public static ViewState.Output AsOutput(this ViewState state) => (ViewState.Output)state;

    public static ViewState ToMenu(this ViewState state) => state switch
    {
        ViewState.Menu menu => menu,
        ViewState.Output output => ViewState.MenuFromOutput(output),
        _ => throw new InvalidOperationException($"Unknown state type: {state.GetType()}")
    };

    public static ViewState ToOutput(this ViewState state, string content) => state switch
    {
        ViewState.Menu menu => ViewState.OutputFromMenu(menu, content),
        ViewState.Output output => output.WithContent(content),
        _ => throw new InvalidOperationException($"Unknown state type: {state.GetType()}")
    };
}