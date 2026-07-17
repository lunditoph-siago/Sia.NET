namespace Sia.Reactors;

using System.Runtime.CompilerServices;

public class Hierarchy<TTag> : ReactorBase<TypeUnion<Node<TTag>>>
{
    public IReadOnlySet<Entity> Root => _root;

    private readonly HashSet<Entity> _root = [];
    private readonly Stack<HashSet<Entity>> _childrenPool = new();

    public override void OnInitialize(World world)
    {
        base.OnInitialize(world);

        Listen((Entity entity, in Node<TTag>.SetParent cmd) => {
            ref var node = ref entity.Get<Node<TTag>>();

            var parent = node.Parent;
            var previousParent = node._prevParent;

            if (previousParent is { } previous) {
                RemoveFromParent(entity, previous);

                if (parent == null) {
                    _root.Add(entity);
                }
                else if (parent is { } current) {
                    parent = AddToParent(entity, current);
                }
            }
            else if (parent is { } current) {
                _root.Remove(entity);
                parent = AddToParent(entity, current);
            }

            SetIsEnabledRecursively(entity, ref node,
                parent is not { } attached || attached.Get<Node<TTag>>().IsEnabled);
            return false;
        });

        Listen((Entity entity, in Node<TTag>.SetIsSelfEnabled cmd) => {
            ref var node = ref entity.Get<Node<TTag>>();
            SetIsEnabledRecursively(entity, ref node,
                node.Parent is not { } parent || parent.Get<Node<TTag>>().IsEnabled);
        });
    }

    private void SetIsEnabledRecursively(Entity entity, ref Node<TTag> node, bool parentEnabled)
    {
        var prevEnabled = node._enabled;
        var enabled = parentEnabled && node.IsSelfEnabled;
        node._enabled = enabled;

        if (prevEnabled == enabled) {
            return;
        }
        World.Send(entity, new Node<TTag>.OnIsEnabledChanged(enabled));

        if (node._children != null) {
            foreach (var child in node._children) {
                ref var childNode = ref child.Get<Node<TTag>>();
                SetIsEnabledRecursively(child, ref childNode, enabled);
            }
        }
    }

    protected override void OnEntityAdded(Entity entity)
    {
        ref var node = ref entity.Get<Node<TTag>>();
        var parent = node.Parent;
        if (parent is { } attached) {
            AddToParent(entity, attached);
        }
        else {
            _root.Add(entity);
        }
    }

    protected override void OnEntityRemoved(Entity entity)
    {
        ref var node = ref entity.Get<Node<TTag>>();

        var parent = node.Parent;
        if (parent is { } attached) {
            RemoveFromParent(entity, attached);
        }
        else {
            _root.Remove(entity);
        }

        var children = node._children;
        if (children != null) {
            foreach (var child in children) {
                child.Destroy();
            }
            children.Clear();
            _childrenPool.Push(children);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Entity AddToParent(Entity entity, Entity parent)
    {
        ref var parentNode = ref parent.Get<Node<TTag>>();
        ref var children = ref parentNode._children;

        children ??= _childrenPool.TryPop(out var pooled) ? pooled : [];
        children.Add(entity);

        World.Send(parent, new Node<TTag>.ChildAdded(entity));
        return parent;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RemoveFromParent(Entity entity, in Entity parent)
    {
        ref var parentNode = ref parent.Get<Node<TTag>>();
        ref var children = ref parentNode._children;

        children!.Remove(entity);

        if (children.Count == 0) {
            _childrenPool.Push(children);
            children = null;
        }

        World.Send(parent, new Node<TTag>.ChildRemoved(entity));
    }
}
