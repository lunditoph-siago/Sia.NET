using System.Runtime.CompilerServices;

namespace Sia_Examples;

public sealed class ViewState
{
    private bool _needsUpdate = true;
    private bool _showingMenu = true;
    private int _selectedExample;
    private int _hoveredExample = -1;
    private string _currentOutput = string.Empty;
    private float _menuScrollOffset;
    private float _outputScrollOffset;

    public ViewMode Mode => _showingMenu ? ViewMode.Menu : ViewMode.Output;

    public required bool ShowingMenu
    {
        get => _showingMenu;
        init => SetField(ref _showingMenu, value);
    }

    public required int SelectedExample
    {
        get => _selectedExample;
        init => SetField(ref _selectedExample, value);
    }

    public required int HoveredExample
    {
        get => _hoveredExample;
        init => SetField(ref _hoveredExample, value);
    }

    public required string CurrentOutput
    {
        get => _currentOutput;
        init => SetField(ref _currentOutput, value);
    }

    public required float MenuScrollOffset
    {
        get => _menuScrollOffset;
        init => SetField(ref _menuScrollOffset, value);
    }

    public required float OutputScrollOffset
    {
        get => _outputScrollOffset;
        init => SetField(ref _outputScrollOffset, value);
    }

    public bool ConsumeUpdateFlag()
    {
        if (!_needsUpdate) return false;
        _needsUpdate = false;
        return true;
    }

    public void MarkDirty() => _needsUpdate = true;

    public ViewState With(Action<ViewStateBuilder> configure)
    {
        var builder = new ViewStateBuilder(this);
        configure(builder);
        return builder.Build();
    }

    public ViewState ResetScroll() => With(builder =>
        builder.SetScrollOffsets(menuOffset: 0f, outputOffset: 0f));

    public ViewState ToMenuMode() => With(builder =>
        builder.SetMode(showingMenu: true).ResetScrollOffsets());

    public ViewState ToOutputMode(string output) => With(builder =>
        builder.SetMode(showingMenu: false)
               .SetCurrentOutput(output)
               .ResetScrollOffsets());

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            _needsUpdate = true;
        }
    }
}

public enum ViewMode
{
    Menu,
    Output
}

public sealed class ViewStateBuilder
{
    private bool _showingMenu;
    private int _selectedExample;
    private int _hoveredExample;
    private string _currentOutput;
    private float _menuScrollOffset;
    private float _outputScrollOffset;

    internal ViewStateBuilder(ViewState source)
    {
        _showingMenu = source.ShowingMenu;
        _selectedExample = source.SelectedExample;
        _hoveredExample = source.HoveredExample;
        _currentOutput = source.CurrentOutput;
        _menuScrollOffset = source.MenuScrollOffset;
        _outputScrollOffset = source.OutputScrollOffset;
    }

    public ViewStateBuilder SetMode(bool showingMenu)
    {
        _showingMenu = showingMenu;
        return this;
    }

    public ViewStateBuilder SetSelectedExample(int index)
    {
        _selectedExample = index;
        return this;
    }

    public ViewStateBuilder SetHoveredExample(int index)
    {
        _hoveredExample = index;
        return this;
    }

    public ViewStateBuilder SetCurrentOutput(string output)
    {
        _currentOutput = output;
        return this;
    }

    public ViewStateBuilder SetScrollOffsets(float menuOffset, float outputOffset)
    {
        _menuScrollOffset = menuOffset;
        _outputScrollOffset = outputOffset;
        return this;
    }

    public ViewStateBuilder ResetScrollOffsets() => SetScrollOffsets(0f, 0f);

    public ViewStateBuilder AdjustMenuScroll(float delta)
    {
        _menuScrollOffset += delta;
        return this;
    }

    public ViewStateBuilder AdjustOutputScroll(float delta)
    {
        _outputScrollOffset += delta;
        return this;
    }

    internal ViewState Build() => new()
    {
        ShowingMenu = _showingMenu,
        SelectedExample = _selectedExample,
        HoveredExample = _hoveredExample,
        CurrentOutput = _currentOutput,
        MenuScrollOffset = _menuScrollOffset,
        OutputScrollOffset = _outputScrollOffset
    };
}