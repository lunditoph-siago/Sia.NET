namespace Sia.Reactors;

using System.Collections;
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

            var parent = node.Parent;
            var previousParent = node._prevParent;

            if (previousParent != null) {
                RemoveFromParent(entity, previousParent.Value);

                if (parent == null) {
                    _root.Add(id);
                }
                else {
                    AddToParent(entity, parent.Value);
                }
            }
            else if (parent != null) {
                _root.Remove(id);
                AddToParent(entity, parent.Value);
            }

            return false;
        });
    }

    protected override void OnEntityAdded(in EntityRef entity)
    {
        ref var node = ref entity.Get<Node<TTag>>();
        _nodes[entity.Id] = entity;

        var parent = node.Parent;
        if (parent != null) {
            AddToParent(entity, parent.Value);
        }
        else {
            _root.Add(entity.Id);
        }
    }

    protected override void OnEntityRemoved(in EntityRef entity)
    {
        var id = entity.Id;
        ref var node = ref entity.Get<Node<TTag>>();

        var parent = node.Parent;
        if (parent != null) {
            RemoveFromParent(entity, parent.Value);
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
    private void AddToParent(in EntityRef entity, Identity parent)
    {
        var parentEntity = _nodes[parent];
        ref var parentNode = ref parentEntity.Get<Node<TTag>>();
        ref var children = ref parentNode._children;

        children ??= _childrenPool.TryPop(out var pooled) ? pooled : [];
        children.Add(entity.Id);

        World.Send(parentEntity, new Node<TTag>.ChildAdded(entity));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RemoveFromParent(in EntityRef entity, in Identity parent)
    {
        var parentEntity = _nodes[parent];
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