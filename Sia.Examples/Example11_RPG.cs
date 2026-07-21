using CommunityToolkit.HighPerformance;
using Sia;

namespace Sia_Examples;

public static partial class Example11_RPG
{
    public enum MagicType
    {
        Fire,
        Wind,
        Water,
        Earth,
    }

    public readonly record struct WeaponDamage(
        string WeaponName,
        MagicType MagicType,
        float PhysicalDamage,
        float ElementalDamage,
        float ElementalMultiplier,
        float ManaSpent)
    {
        public float Total => PhysicalDamage + ElementalDamage;
    }

    public interface IWeaponDamageProvider
    {
        public WeaponDamage GetDamage(
            in WeaponMetadata metadata,
            Entity weapon,
            Entity attacker,
            Entity target,
            World world);
    }

    public readonly record struct WeaponMetadata(
        string Name,
        float BaseDamage,
        float MagicDamage,
        float ManaCost,
        IWeaponDamageProvider DamageProvider);

    public static class MagicSword
    {
        private sealed class DamageProvider : IWeaponDamageProvider
        {
            public static readonly DamageProvider Instance = new();

            public WeaponDamage GetDamage(
                in WeaponMetadata metadata,
                Entity weapon,
                Entity attacker,
                Entity target,
                World world)
            {
                var enchantment = weapon.Get<Enchantment>();
                var attackerView = new Character.View(attacker, world);
                if (attackerView.MP < metadata.ManaCost) {
                    return new(
                        metadata.Name,
                        enchantment.MagicType,
                        metadata.BaseDamage,
                        ElementalDamage: 0,
                        ElementalMultiplier: 1,
                        ManaSpent: 0);
                }

                attackerView.MP -= metadata.ManaCost;

                ref readonly var targetMetadata =
                    ref target.Get<CharacterMetadata>();
                var multiplier = targetMetadata.Weakness == enchantment.MagicType
                    ? 1.5f
                    : 1f;
                return new(
                    metadata.Name,
                    enchantment.MagicType,
                    metadata.BaseDamage,
                    metadata.MagicDamage * multiplier,
                    multiplier,
                    metadata.ManaCost);
            }
        }

        public readonly record struct Enchantment(MagicType MagicType);

        public static readonly WeaponMetadata Metadata = new(
            Name: "Magic Sword",
            BaseDamage: 10f,
            MagicDamage: 20f,
            ManaCost: 10f,
            DamageProvider: DamageProvider.Instance);

        public static Entity Create(World world, MagicType magicType)
            => world.Create(HList.From(Metadata, new Enchantment(magicType)));
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
        float DefenseGrowthRate,
        MagicType? Weakness)
    {
        public float MaxHP(int level) => BaseHP + level * HPGrowthRate;
        public float MaxMP(int level) => BaseMP + level * MPGrowthRate;
        public float Damage(int level) => BaseDamage + level * DamageGrowthRate;
        public float Defense(int level) => BaseDefense + level * DefenseGrowthRate;

        public Character CreateCharacter(string name, int level)
            => new(
                Name: name,
                HP: MaxHP(level),
                MP: MaxMP(level),
                Level: level);
    }

    public partial record struct Character(
        [Sia] string Name,
        [Sia] float HP,
        [Sia] float MP,
        [Sia] int Level = 0,
        [Sia] Entity? Weapon = null)
    {
        public readonly record struct Damage(Entity Attacker) : ICommand
        {
            public void Execute(World world, Entity target)
            {
                if (!IsCharacter(Attacker)) {
                    Console.WriteLine("Attack ignored: attacker is not a valid character.");
                    return;
                }
                if (!IsCharacter(target)) {
                    Console.WriteLine("Attack ignored: target is not a valid character.");
                    return;
                }

                ref readonly var attackerMetadata =
                    ref Attacker.Get<CharacterMetadata>();
                ref readonly var targetMetadata =
                    ref target.Get<CharacterMetadata>();
                var attacker = new View(Attacker, world);
                var defender = new View(target, world);

                var characterDamage = attackerMetadata.Damage(attacker.Level);
                WeaponDamage? weaponDamage = null;
                if (attacker.Weapon is Entity { IsValid: true } weapon) {
                    if (weapon.Contains<WeaponMetadata>()) {
                        ref readonly var metadata = ref weapon.Get<WeaponMetadata>();
                        weaponDamage = metadata.DamageProvider.GetDamage(
                            metadata,
                            weapon,
                            Attacker,
                            target,
                            world);
                    } else {
                        Console.WriteLine(
                            $"  {attacker.Name}'s equipped entity is not a weapon.");
                    }
                }

                var rawDamage = characterDamage + (weaponDamage?.Total ?? 0);
                var defense = targetMetadata.Defense(defender.Level);
                var actualDamage = MathF.Max(0, rawDamage - defense);
                var previousHP = defender.HP;
                var nextHP = MathF.Max(0, previousHP - actualDamage);

                Console.WriteLine(
                    $"{attacker.Name} attacks {defender.Name}: "
                    + $"{rawDamage:0.#} attack - {defense:0.#} defense "
                    + $"= {actualDamage:0.#} damage");
                if (weaponDamage is { } contribution) {
                    var magic = contribution.ElementalDamage > 0
                        ? $" + {contribution.ElementalDamage:0.#} "
                            + $"{contribution.MagicType} magic "
                            + $"(x{contribution.ElementalMultiplier:0.#})"
                        : " + no magic (insufficient MP)";
                    Console.WriteLine(
                        $"  {contribution.WeaponName}: "
                        + $"{contribution.PhysicalDamage:0.#} physical"
                        + magic
                        + $", {contribution.ManaSpent:0.#} MP spent");
                }
                Console.WriteLine(
                    $"  {defender.Name} HP: {previousHP:0.#} -> {nextHP:0.#}; "
                    + $"{attacker.Name} MP: {attacker.MP:0.#}");

                defender.HP = nextHP;
            }

            private static bool IsCharacter(Entity entity)
                => entity.IsValid
                    && entity.Contains<Character>()
                    && entity.Contains<CharacterMetadata>();
        }
    }

    public sealed class DeadCharacterDestroySystem() : SystemBase(
        Matchers.Of<Character>(),
        EventUnion.Of<Character.SetHP>())
    {
        private readonly List<Entity> _entitiesToDestroy = [];

        public override void Execute(World world, IEntityQuery query)
        {
            foreach (var entity in query) {
                ref var character = ref entity.Get<Character>();
                if (character.HP <= 0) {
                    Console.WriteLine($"{character.Name} is dead!");
                    _entitiesToDestroy.Add(entity);
                }
            }

            if (_entitiesToDestroy.Count == 0) {
                return;
            }
            foreach (var entity in _entitiesToDestroy.AsSpan()) {
                entity.Destroy();
            }
            _entitiesToDestroy.Clear();
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
            DefenseGrowthRate: 4f,
            Weakness: MagicType.Water);

        public static Entity Create(World world, string name, int level = 0)
            => world.Create(HList.From(
                Metadata,
                Metadata.CreateCharacter(name, level)));
    }

    public static class Goblin
    {
        public static readonly CharacterMetadata Metadata = new(
            Species: "Goblin",
            BaseHP: 140f,
            BaseMP: 0f,
            HPGrowthRate: 10f,
            MPGrowthRate: 0f,
            BaseDamage: 20f,
            BaseDefense: 3f,
            DamageGrowthRate: 4f,
            DefenseGrowthRate: 2f,
            Weakness: MagicType.Fire);

        public static Entity Create(World world, string name, int level = 0)
            => world.Create(HList.From(
                Metadata,
                Metadata.CreateCharacter(name, level)));
    }

    public static void Run(World world)
    {
        using var stage = SystemChain.Empty
            .Add<DeadCharacterDestroySystem>()
            .CreateStage(world);

        var player = Human.Create(world, "Player", level: 3);
        var enemy = Goblin.Create(world, "Fire-weak Goblin", level: 2);
        var magicSword = MagicSword.Create(world, MagicType.Fire);

        var playerView = new Character.View(player, world) {
            Weapon = magicSword,
        };
        PrintCharacter(player, world);
        PrintCharacter(enemy, world);

        for (var round = 1; player.IsValid && enemy.IsValid; round++) {
            Console.WriteLine($"-- Round {round} --");
            enemy.Execute(new Character.Damage(player));
            stage.Tick();
            if (!enemy.IsValid) {
                break;
            }

            player.Execute(new Character.Damage(enemy));
            stage.Tick();
        }

        Console.WriteLine("-- Result --");
        if (player.IsValid) {
            PrintCharacter(player, world);
        }
        if (enemy.IsValid) {
            PrintCharacter(enemy, world);
        }
    }

    private static void PrintCharacter(Entity entity, World world)
    {
        ref readonly var metadata = ref entity.Get<CharacterMetadata>();
        var character = new Character.View(entity, world);
        Console.WriteLine(
            $"{character.Name} [{metadata.Species}, Lv.{character.Level}] "
            + $"HP {character.HP:0.#}, MP {character.MP:0.#}, "
            + $"ATK {metadata.Damage(character.Level):0.#}, "
            + $"DEF {metadata.Defense(character.Level):0.#}");
    }
}
