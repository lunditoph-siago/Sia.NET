namespace Sia.Reactors;

using System.Runtime.CompilerServices;

public class Hierarchy<TTag> : ReactorBase<TypeUnion<Node<TTag>>>
{
    public IReadOnlyDictionary<Identity, EntityRef> Nodes => _nodes;
    public IReadOnlySet<Identity> Root => _root;

    private readonly EntityMap _nodes = [];
    private readonly HashSet<Identity> _root = [];
    private readonly Stack<HashSet<Identity>> _childrenPool = new();

    public override void OnInitialize(World world)
    {
        base.OnInitialize(world);
        
        Listen((in EntityRef entity, in Node<TTag>.SetParent cmd) => {
            var id = entity.Id;
            ref var node = ref entity.Get<Node<TTag>>();

            var parentId = node.Parent;
            var previousParentId = node._prevParent;
            EntityRef? parent = null;

            if (previousParentId != null) {
                RemoveFromParent(entity, previousParentId.Value);

                if (parentId == null) {
                    _root.Add(id);
                }
                else {
                    parent = AddToParent(entity, parentId.Value);
                }
            }
            else if (parentId != null) {
                _root.Remove(id);
                parent = AddToParent(entity, parentId.Value);
            }

            SetIsEnabledRecursively(entity, ref node,
                !parent.HasValue || parent.Value.Get<Node<TTag>>().IsEnabled);
            return false;
        });

        Listen((in EntityRef entity, in Node<TTag>.SetIsSelfEnabled cmd) => {
            ref var node = ref entity.Get<Node<TTag>>();
            SetIsEnabledRecursively(entity, ref node,
                node.Parent == null || Nodes[node.Parent.Value].Get<Node<TTag>>().IsEnabled);
        });
    }

    private void SetIsEnabledRecursively(EntityRef entity, ref Node<TTag> node, bool parentEnabled)
    {
        var prevEnabled = node._enabled;
        var enabled = parentEnabled && node.IsSelfEnabled;
        node._enabled = enabled;
        
        if (prevEnabled == enabled) {
            return;
        }
        World.Send(entity, new Node<TTag>.OnIsEnabledChanged(enabled));

        if (node._children != null) {
            foreach (var childId in node._children) {
                var child = Nodes[childId];
                ref var childNode = ref child.Get<Node<TTag>>();
                SetIsEnabledRecursively(child, ref childNode, enabled);
            }
        }
    }

    protected override void OnEntityAdded(in EntityRef entity)
    {
        ref var node = ref entity.Get<Node<TTag>>();
        _nodes[entity.Id] = entity;

        var parentId = node.Parent;
        if (parentId != null) {
            AddToParent(entity, parentId.Value);
        }
        else {
            _root.Add(entity.Id);
        }
    }

    protected override void OnEntityRemoved(in EntityRef entity)
    {
        var id = entity.Id;
        ref var node = ref entity.Get<Node<TTag>>();

        var parentId = node.Parent;
        if (parentId != null) {
            RemoveFromParent(entity, parentId.Value);
        }
        else {
            _root.Remove(id);
        }

        var children = node._children;
        if (children != null) {
            foreach (var child in children) {
                _nodes[child].Dispose();
            }
            children.Clear();
            _childrenPool.Push(children);
        }

        _nodes.Remove(id);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private EntityRef AddToParent(in EntityRef entity, Identity parentId)
    {
        var parentEntity = _nodes[parentId];
        ref var parentNode = ref parentEntity.Get<Node<TTag>>();
        ref var children = ref parentNode._children;

        children ??= _childrenPool.TryPop(out var pooled) ? pooled : [];
        children.Add(entity.Id);

        World.Send(parentEntity, new Node<TTag>.ChildAdded(entity));
        return parentEntity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RemoveFromParent(in EntityRef entity, in Identity parentId)
    {
        var parentEntity = _nodes[parentId];
        ref var parentNode = ref parentEntity.Get<Node<TTag>>();
        ref var children = ref parentNode._children;

        children!.Remove(entity.Id);

        if (children.Count == 0) {
            _childrenPool.Push(children);
            children = null;
        }

        World.Send(parentEntity, new Node<TTag>.ChildRemoved(entity));
    }
}