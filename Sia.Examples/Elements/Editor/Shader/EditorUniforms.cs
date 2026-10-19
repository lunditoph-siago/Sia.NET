using System.Reflection;
using System.Text.Json;
using Sia.Math;
using Sia.Reactive;
#if BROWSER
using System.Runtime.InteropServices.JavaScript;
#endif

namespace Sia.Examples.Editor;

public static class EditorUniformHooks
{
    private readonly record struct Dependencies<T>(EditorUniformSession Session, string Name, State<T> State)
        where T : unmanaged;

    public static T UseUniforms<T>(this ref Hooks hooks, in EditorPipelineFrame frame, string name, T initial)
        where T : unmanaged
    {
        var session = frame.Uniforms ?? throw new InvalidOperationException("Uniforms require an editor pipeline session.");
        var state = hooks.UseState(initial);
        hooks.UseEffect(new Dependencies<T>(session, name, state),
            static (in Dependencies<T> d) => d.Session.Register(d.Name, d.State),
            static (in IDisposable registration) => registration.Dispose());
        return state.Value;
    }
}

internal sealed partial class EditorUniformInspector : IDisposable
{
#if BROWSER
    internal JSObject Handle { get; } = Create("editor-uniform-inspector");
    [JSImport("createUniformInspector", "main.js")]
    private static partial JSObject Create(string containerId);
    [JSImport("disposeUniformInspector", "main.js")]
    private static partial void Release(JSObject owner);
#endif
    public void Dispose()
    {
#if BROWSER
        Release(Handle);
        Handle.Dispose();
#endif
    }
}

internal sealed partial class EditorUniformSession : IDisposable
{
    private static readonly JsonSerializerOptions s_JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, Registration> _registrations = [];
    private bool _dirty;
#if BROWSER
    private readonly JSObject _binding;
    public EditorUniformSession(EditorUniformInspector inspector) => _binding = Create(inspector.Handle);
#else
    public EditorUniformSession(EditorUniformInspector inspector) { }
#endif

    public IDisposable Register<T>(string name, State<T> state) where T : unmanaged
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var registration = new Registration(this, name, UniformField.Describe(typeof(T)),
            () => state.Value, value => state.Set((T)value));
        if (!_registrations.TryAdd(name, registration))
            throw new InvalidOperationException($"Uniform group '{name}' is already registered. Use a distinct name per mounted component.");
        _dirty = true;
        return registration;
    }

    public void ApplyEdits()
    {
#if BROWSER
        var json = Read(_binding);
        if (json.Length > 0) {
            var edits = JsonSerializer.Deserialize<Dictionary<string, double[]>>(json)!;
            foreach (var registration in _registrations.Values) {
                var value = registration.Read();
                var changed = false;
                foreach (var field in registration.Fields) {
                    if (!edits.TryGetValue(registration.Key(field), out var numbers)) continue;
                    value = field.Write(value, numbers);
                    changed = true;
                }
                if (changed) registration.Write(value);
            }
        }
        ApplyImageEdits();
#endif
    }

    public void Sync()
    {
        if (!_dirty) return;
        _dirty = false;
#if BROWSER
        var fields = _registrations.Values.SelectMany(registration => registration.Fields.Select(field => new {
            key = registration.Key(field), name = registration.Name + "." + field.Name,
            type = field.Type, values = field.Read(registration.Read()), color = field.Color,
            min = field.Minimum, max = field.Maximum,
        }));
        Configure(_binding, JsonSerializer.Serialize(fields.Cast<object>().Concat(ImageFields()), s_JsonOptions));
#endif
    }

    public void Activate()
    {
        Sync();
#if BROWSER
        ActivateBinding(_binding);
#endif
    }

    public void Dispose()
    {
        _images.Clear();
#if BROWSER
        Release(_binding);
        _binding.Dispose();
#endif
    }

    private sealed class Registration(EditorUniformSession owner, string name, UniformField[] fields,
        Func<object> read, Action<object> write) : IDisposable
    {
        public string Name { get; } = name;
        public UniformField[] Fields { get; } = fields;
        public Func<object> Read { get; } = read;
        public Action<object> Write { get; } = write;
        public string Key(UniformField field) => Name + "/" + Read().GetType().FullName + "/" + field.Name + "/" + field.Type + "/" + field.Components;
        public void Dispose()
        {
            owner._registrations.Remove(Name);
            owner._dirty = true;
        }
    }

#if BROWSER
    [JSImport("createUniformBinding", "main.js")]
    private static partial JSObject Create(JSObject inspector);
    [JSImport("configureUniformBinding", "main.js")]
    private static partial void Configure(JSObject binding, string json);
    [JSImport("readUniformEdits", "main.js")]
    private static partial string Read(JSObject binding);
    [JSImport("activateUniformBinding", "main.js")]
    private static partial void ActivateBinding(JSObject binding);
    [JSImport("disposeUniformBinding", "main.js")]
    private static partial void Release(JSObject binding);
#endif
}

internal sealed class UniformField(string name, string type, FieldInfo[] path, int components,
    bool color, double? minimum, double? maximum)
{
    public string Name { get; } = name;
    public string Type { get; } = type;
    public int Components => components;
    public bool Color { get; } = color;
    public double? Minimum { get; } = minimum;
    public double? Maximum { get; } = maximum;

    public double[] Read(object value)
    {
        var current = value;
        foreach (var field in path) current = field.GetValue(current)!;
        return current switch {
            float2 v => [v.x, v.y],
            float3 v => [v.x, v.y, v.z],
            float4 v => [v.x, v.y, v.z, v.w],
            _ => [Convert.ToDouble(current, System.Globalization.CultureInfo.InvariantCulture)],
        };
    }

    public object Write(object value, double[] numbers)
    {
        if (numbers.Length != Components) throw new InvalidDataException("Uniform component count changed.");
        var minimum = Type == "f32" && Minimum is { } min ? (double)(float)min : Minimum;
        var maximum = Type == "f32" && Maximum is { } max ? (double)(float)max : Maximum;
        foreach (var number in numbers) {
            var represented = Type == "f32" ? (double)(float)number : number;
            if (!double.IsFinite(number) || represented < minimum || represented > maximum
                || Type == "f32" && System.Math.Abs(number) > float.MaxValue
                || Type == "i32" && (number < int.MinValue || number > int.MaxValue || number != System.Math.Truncate(number))
                || Type == "u32" && (number < 0 || number > uint.MaxValue || number != System.Math.Truncate(number))
                || Type == "bool" && number is not (0 or 1))
                throw new InvalidDataException($"Invalid {Type} value for {Name}.");
        }
        var scalar = Components switch {
            2 => new float2((float)numbers[0], (float)numbers[1]),
            3 => new float3((float)numbers[0], (float)numbers[1], (float)numbers[2]),
            4 => new float4((float)numbers[0], (float)numbers[1], (float)numbers[2], (float)numbers[3]),
            _ => Type switch { "f32" => (object)(float)numbers[0], "i32" => (int)numbers[0], "u32" => (uint)numbers[0], _ => numbers[0] != 0 },
        };
        return Set(value, path, 0, scalar);
    }

    private static object Set(object value, FieldInfo[] path, int index, object scalar)
    {
        var field = path[index];
        field.SetValue(value, index + 1 == path.Length ? scalar : Set(field.GetValue(value)!, path, index + 1, scalar));
        return value;
    }

    public static UniformField[] Describe(Type type)
    {
        var result = new List<UniformField>();
        Visit(type, [], "");
        if (result.Count == 0) throw new ArgumentException("Uniforms require public mutable numeric or boolean fields.");
        return [.. result];

        void Visit(Type current, FieldInfo[] prefix, string name)
        {
            foreach (var field in current.GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(field => field.MetadataToken)) {
                if (field.IsInitOnly) throw new ArgumentException($"Uniform field {name + field.Name} must be mutable.");
                var path = prefix.Append(field).ToArray();
                var fieldName = name + field.Name;
                var range = field.GetCustomAttribute<Sia.RangeAttribute>();
                var color = field.IsDefined(typeof(Sia.ColorAttribute));
                if (range is not null && (!double.IsFinite(range.Minimum) || !double.IsFinite(range.Maximum) || range.Minimum >= range.Maximum))
                    throw new ArgumentException($"Invalid range for {fieldName}.");
                var scalar = Scalar(field.FieldType);
                var components = field.FieldType == typeof(float2) ? 2 : field.FieldType == typeof(float3) ? 3
                    : field.FieldType == typeof(float4) ? 4 : 1;
                if (color && components < 3)
                    throw new ArgumentException($"Color requires float3 or float4: {fieldName}.");
                if (range is not null && scalar == "bool") throw new ArgumentException($"Range requires a numeric field: {fieldName}.");
                if (scalar is not null || components > 1) {
                    result.Add(new(fieldName, scalar ?? "f32", path, components, color, range?.Minimum, range?.Maximum));
                }
                else if (range is not null) throw new ArgumentException($"Range requires a numeric field: {fieldName}.");
                else if (field.FieldType.IsValueType && !field.FieldType.IsPrimitive && !field.FieldType.IsEnum)
                    Visit(field.FieldType, path, fieldName + ".");
                else throw new ArgumentException($"Unsupported uniform field {fieldName}: {field.FieldType.Name}.");
            }
        }
    }

    private static string? Scalar(Type type) => type == typeof(float) ? "f32" : type == typeof(int) ? "i32"
        : type == typeof(uint) ? "u32" : type == typeof(bool) ? "bool" : null;
}
