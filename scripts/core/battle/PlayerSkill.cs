using Godot;

namespace MementoTest.Resources
{
	public enum AttackType
	{
		Unarmed,
		Melee,
		RangedBow,
		RangedMagic
	}

	[GlobalClass]
	public partial class PlayerSkill : Resource
	{
		[Export] public string CommandName { get; set; } = "ping";
		[Export] public int ApCost { get; set; } = 2;
		[Export] public int Damage { get; set; } = 10;

		[Export] public AttackType AttackType { get; set; } = AttackType.Unarmed;

		// Hanya dipakai untuk ranged
		[Export] public PackedScene ProjectileScene;

		[Export(PropertyHint.MultilineText)]
		public string Description { get; set; } = "Basic attack";
	}
}
