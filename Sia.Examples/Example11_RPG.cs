namespace Sia_Examples;

using CommunityToolkit.HighPerformance;
using Sia;

public static partial class Example11_RPG
{
    public interface IWeaponDamageProvider
    {
        public float GetDamage(in WeaponMetadata metadata, Entity attacker, Entity target);
    }

    public readonly record struct WeaponMetadata(
        string Name,
        float BaseDamage,
        IWeaponDamageProvider DamageProvider);
    
    public enum MagicType
    {
        Fire,
        Wind,
        Water,
        Earth
    }

    public static class MagicSword
    {
        private class DamageProvider : IWeaponDamageProvider
        {
            public static readonly DamageProvider Instance = new();

            public float GetDamage(in WeaponMetadata metadata, Entity attacker, Entity target)
            {
                float damage = metadata.BaseDamage + Random.Shared.NextSingle() * 10f;

                ref var attackerCharacter = ref attacker.Get<Character>();
                if (attackerCharacter.MP > 10f) {
                    attackerCharacter.MP -= 10f;
                    damage += 20f;
                }

                return damage;
            }
        }

        public readonly record struct Data(
            MagicType MagicType // not used
        );

        public static readonly WeaponMetadata Metadata = new(
            Name: "Magic Sword",
            BaseDamage: 10f,
            DamageProvider: DamageProvider.Instance);

        public static Entity Create(World world, MagicType magicType)
            => world.Create(HList.From(
                Metadata,
                new Data(magicType)
            ));
    }

    public readonly record struct CharacterMetadata(
        string Species,

        float BaseHP,
        float BaseMP,
        float HPGrowthRate,
        float MPGrowthRate,

        float BaseDamage,
        float BaseDefense,
        float DamageGrowthRate,
        float DefenseGrowthRate);
    
    public partial record struct Character(
        [Sia] string Name,
        [Sia] float HP,
        [Sia] float MP,
        [Sia] int Level = 0,
        [Sia] Entity? Weapon = null)
    {
        public readonly record struct Damage(Entity Attacker) : ICommand
        {
            public void Execute(World world, Entity self)
            {
                float damage;
                float defense;

                try {
                    ref var attackerMeta = ref Attacker.Get<CharacterMetadata>();
                    ref var attackerCharacter = ref Attacker.Get<Character>();
                    damage = attackerMeta.BaseDamage + attackerCharacter.Level * attackerMeta.DamageGrowthRate;

                    if (attackerCharacter.Weapon is Entity weapon) {
                        ref var weaponMeta = ref weapon.Get<WeaponMetadata>();
                        damage += weaponMeta.DamageProvider.GetDamage(weaponMeta, Attacker, self);
                    }
                }
                catch {
                    Console.WriteLine("Failed to execute attack command: attacker is not a valid character");
                    return;
                }

                try {
                    ref var selfMeta = ref self.Get<CharacterMetadata>();

                    var selfCharacter = new View(self);
                    defense = selfMeta.BaseDefense + selfCharacter.Level * selfMeta.DefenseGrowthRate;

                    float actualDamage = damage - defense;
                    if (actualDamage < 0f) {
                        return;
                    }

                    selfCharacter.HP -= actualDamage;
                }
                catch {
                    Console.WriteLine("Failed to execute attack command: self is not a valid character");
                    return;
                }
            }
        }
    }

    public class DeadCharacterDestroySystem() : SystemBase(
        Matchers.Of<Character>(),
        EventUnion.Of<Character.SetHP>())
    {
        private List<Entity> _entitiesToDestroy = [];

        public override void Execute(World world, IEntityQuery query)
        {
            foreach (var entity in query) {
                ref var character = ref entity.Get<Character>();
                if (character.HP <= 0) {
                    Console.WriteLine(character.Name + " is dead!");
                    _entitiesToDestroy.Add(entity);
                }
            }

            if (_entitiesToDestroy.Count != 0) {
                foreach (var entity in _entitiesToDestroy.AsSpan()) {
                    entity.Destroy();
                }
                _entitiesToDestroy.Clear();
            }
        }
    }

    public static class Human
    {
        public static readonly CharacterMetadata Metadata = new(
            Species: "Human",
            BaseHP: 100f,
            BaseMP: 50f,
            HPGrowthRate: 10f,
            MPGrowthRate: 7f,
            BaseDamage: 10f,
            BaseDefense: 6f,
            DamageGrowthRate: 5f,
            DefenseGrowthRate: 4f);

        public static Entity Create(World world, string name)
            => world.Create(HList.From(
                Metadata,
                new Character(
                    Name: name,
                    HP: Metadata.BaseHP,
                    MP: Metadata.BaseMP)
            ));
    }

    public static void Run(World world)
    {
        var stage = SystemChain.Empty
            .Add<DeadCharacterDestroySystem>()
            .CreateStage(world);

        var player = Human.Create(world, "Player");
        var enemy = Human.Create(world, "Enemy");

        var magicSword = MagicSword.Create(world, MagicType.Fire);
        _ = new Character.View(player) {
            Level = 100,
            Weapon = magicSword
        };

        enemy.Execute(new Character.Damage(player));
        stage.Tick();
    }
}