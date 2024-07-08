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

            if (previousParent != null) {
                RemoveFromParent(entity, previousParent);

                if (parent == null) {
                    _root.Add(entity);
                }
                else {
                    parent = AddToParent(entity, parent);
                }
            }
            else if (parent != null) {
                _root.Remove(entity);
                parent = AddToParent(entity, parent);
            }

            SetIsEnabledRecursively(entity, ref node,
                parent == null || parent.Get<Node<TTag>>().IsEnabled);
            return false;
        });

        Listen((Entity entity, in Node<TTag>.SetIsSelfEnabled cmd) => {
            ref var node = ref entity.Get<Node<TTag>>();
            SetIsEnabledRecursively(entity, ref node,
                node.Parent == null || node.Parent.Get<Node<TTag>>().IsEnabled);
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
        if (parent != null) {
            AddToParent(entity, parent);
        }
        else {
            _root.Add(entity);
        }
    }

    protected override void OnEntityRemoved(Entity entity)
    {
        ref var node = ref entity.Get<Node<TTag>>();

        var parent = node.Parent;
        if (parent != null) {
            RemoveFromParent(entity, parent);
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