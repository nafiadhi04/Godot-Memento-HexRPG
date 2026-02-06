using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using MementoTest.Entities;
using MementoTest.Resources;
using MementoTest.Core;
using MementoTest.UI;

namespace MementoTest.Entities
{
	public partial class EnemyController : CharacterBody2D
	{
		/* =======================
		 * EXPORT / CONFIG
		 * ======================= */
		[ExportGroup("Stats")]
		[Export] public int MaxHP = 50;

		[ExportGroup("Movement")]
		[Export] public float MoveDuration = 0.3f;

		[ExportGroup("Combat")]
		[Export] public Godot.Collections.Array<EnemySkill> SkillList;
		[Export] public float ReactionTimeMelee = 1.5f;
		[Export] public float ReactionTimeRanged = 2.0f;
		[Export] public PackedScene DamagePopupScene;

		[ExportGroup("AI")]
		[Export] public EnemyAIProfile AiProfile;
		[Export] public TargetingProfile TargetingProfile;

		[ExportGroup("Scoring")]
		[Export] public SpeedBonusProfile SpeedBonusProfile;


		/* =======================
		 * STATE
		 * ======================= */
		private int _currentHP;
		private bool _isBusy;
		
		private PlayerController _targetPlayer;

		private MapManager _mapManager;
		private Vector2I _currentGridPos;
		private ProgressBar _healthBar;
		private BattleHUD _hud;
		private bool _isExecutingTurn = false;
		private bool _isAttacking = false;


		private readonly Random _rng = new();

		private readonly TileSet.CellNeighbor[] _hexNeighbors =
		{
			TileSet.CellNeighbor.TopSide,
			TileSet.CellNeighbor.BottomSide,
			TileSet.CellNeighbor.TopLeftSide,
			TileSet.CellNeighbor.TopRightSide,
			TileSet.CellNeighbor.BottomLeftSide,
			TileSet.CellNeighbor.BottomRightSide
		};

		/* =======================
		 * LIFECYCLE
		 * ======================= */
		public override void _Ready()
		{
			_currentHP = MaxHP;

			_healthBar = GetNodeOrNull<ProgressBar>("HealthBar");
			if (_healthBar != null)
			{
				_healthBar.MaxValue = MaxHP;
				_healthBar.Value = _currentHP;
			}

			_mapManager = MapManager.Instance;
			_currentGridPos = _mapManager.WorldToGrid(GlobalPosition);
			GlobalPosition = _mapManager.GridToWorld(_currentGridPos);

			_targetPlayer = GetTree().GetFirstNodeInGroup("Player") as PlayerController;
			_hud = GetTree().GetFirstNodeInGroup("HUD") as BattleHUD;

			AddToGroup("Enemy");
		}
		private PlayerController SelectTarget()
		{
			if (TargetingProfile == null)
			{
				GD.PrintErr("[AI] Missing TargetingProfile, fallback to closest target.");
				return GetClosestPlayer();
			}

			var players = GetTree()
				.GetNodesInGroup("Player")
				.OfType<PlayerController>()
				.Where(p => GodotObject.IsInstanceValid(p))
				.ToList();

			if (players.Count == 0)
			{
				GD.PrintErr("[AI] No valid player targets found.");
				return null;
			}

			return TargetingProfile.Rule switch
			{
				TargetingProfile.TargetRule.Closest =>
					players.OrderBy(p =>
						GlobalPosition.DistanceTo(p.GlobalPosition)
					).First(),

				TargetingProfile.TargetRule.LowestHP =>
					players.OrderBy(p => p.CurrentHP).First(),

				TargetingProfile.TargetRule.Random =>
					players[_rng.Next(players.Count)],

				_ => players.First()
			};
		}
		private PlayerController GetClosestPlayer()
		{
			var players = GetTree()
				.GetNodesInGroup("Player")
				.OfType<PlayerController>()
				.Where(p => GodotObject.IsInstanceValid(p))
				.ToList();

			if (players.Count == 0)
				return null;

			return players
				.OrderBy(p => GlobalPosition.DistanceTo(p.GlobalPosition))
				.First();
		}


		/* =======================
		 * TURN ENTRY POINT
		 * ======================= */
		public async Task ExecuteTurn()
		{
			// Tunggu sampai HUD benar-benar idle
			while (_hud != null && _hud.IsBusy)
			{
				await ToSignal(GetTree().CreateTimer(0.05f), "timeout");
			}

			if (_isExecutingTurn) return;
			_isExecutingTurn = true;

			GD.Print("[AI] Enemy TURN START");

			if (_targetPlayer == null || AiProfile == null)
			{
				_isExecutingTurn = false;
				return;
			}
			_targetPlayer = SelectTarget();

			if (_targetPlayer == null || AiProfile == null)
			{
				GD.PrintErr("[ENEMY] Missing target or AI profile.");
				return;
			}

			GD.Print($"[AI] Target selected: {_targetPlayer.Name}");

			await HandleAI();

			_isExecutingTurn = false;
		}


		private async Task HandleAI()
		{
			int gridDist = GetGridDistanceToPlayer();

			GD.Print($"[AI] GridDist = {gridDist}");

			switch (AiProfile.Behavior)
			{
				case EnemyAIProfile.BehaviorType.Aggressive:
					await HandleAggressive(gridDist);
					break;

				case EnemyAIProfile.BehaviorType.Kiting:
					await HandleKiting(gridDist);
					break;
					
			}
		}


		private async Task HandleAggressive(int gridDist)
		{
			GD.Print($"[AI] Aggressive | GridDist = {gridDist}");

			// 1. Kejar player
			if (gridDist > AiProfile.PreferredDistance)
			{
				GD.Print("[AI] Aggressive: chasing player");
				await MoveTowardsPlayer();
				return;
			}

			// 2. Pastikan HUD siap
			while (_hud != null && _hud.IsBusy)
			{
				await ToSignal(GetTree().CreateTimer(0.05f), "timeout");
			}

			// 3. Serang
			GD.Print("[AI] Aggressive: in range, attacking");
			await TryAttack(gridDist);
		}


		private async Task HandleKiting(int gridDist)
		{
			GD.Print($"[AI] Kiting | GridDist = {gridDist}");

			// 1. Terlalu dekat → mundur
			if (gridDist < AiProfile.RetreatDistance)
			{
				GD.Print("[AI] Kiting: retreating");
				await MoveAwayFromPlayer();
				return;
			}

			// 2. Ideal → serang
			if (gridDist <= AiProfile.PreferredDistance)
			{
				while (_hud != null && _hud.IsBusy)
				{
					await ToSignal(GetTree().CreateTimer(0.05f), "timeout");
				}

				GD.Print("[AI] Kiting: attacking");
				await TryAttack(gridDist);
				return;
			}

			// 3. Terlalu jauh → kejar
			if (gridDist <= AiProfile.ChaseDistance)
			{
				GD.Print("[AI] Kiting: chasing");
				await MoveTowardsPlayer();
				return;
			}

			// 4. Diam
			await Wait();
		}


		private async Task TryAttack(int dist)
		{
			var usableSkills = SkillList
				.Where(s => s.AttackRange >= dist)
				.ToList();

			if (usableSkills.Count == 0)
			{
				GD.Print("[AI] No usable skills, waiting");
				await Wait();
				return;
			}

			if (_hud != null && _hud.IsBusy)
			{
				GD.Print("[AI] HUD busy, delaying attack");
				await Wait();
				return;
			}

			var skill = usableSkills[_rng.Next(usableSkills.Count)];
			await PerformSkill(skill);

			
		}

		private async Task TryAttackPixel()
		{
			float pixelDist = GlobalPosition.DistanceTo(_targetPlayer.GlobalPosition);

			var skills = SkillList.Where(s => s.AttackRange >= pixelDist).ToList();
			if (skills.Count == 0)
			{
				await Wait();
				return;
			}

			await PerformSkill(skills[_rng.Next(skills.Count)]);
		}



		private async Task PerformSkill(EnemySkill skill)
		{
			if (_isAttacking) return;
			_isAttacking = true;

			Vector2 startPos = GlobalPosition;
			Vector2 dir = startPos.DirectionTo(_targetPlayer.GlobalPosition);

			var t = CreateTween();
			t.TweenProperty(this, "global_position", startPos + dir * 30f, 0.2f);
			await ToSignal(t, "finished");

			bool success = false;

			if (_hud != null)
			{
				bool melee = skill.AttackRange <= AiProfile.MeleeThreshold;
				string word = melee ? "parry" : "dodge";
				float time = melee ? ReactionTimeMelee : ReactionTimeRanged;

				success = await _hud.WaitForPlayerReaction(word, time);
			}

			// 🔥 SATU-SATUNYA DAMAGE CALL
			int finalDamage = success ? 0 : skill.Damage;
			_targetPlayer.TakeDamage(finalDamage);

			var back = CreateTween();
			back.TweenProperty(this, "global_position", startPos, 0.2f);
			await ToSignal(back, "finished");

			if (success && SpeedBonusProfile != null && _hud != null)
			{
				float reactionTime = _hud.LastReactionTime;
				int bonus = SpeedBonusProfile.CalculateBonus(reactionTime);

				if (bonus > 0)
				{
					ScoreManager.Instance?.AddScore(bonus);
					GD.Print($"[SPEED BONUS] +{bonus} ({reactionTime:0.00}s)");
				}
			}

			_isAttacking = false;
		}

		/* =======================
		 * MOVEMENT
		 * ======================= */
		private List<Vector2I> GetValidNeighbors()
		{
			List<Vector2I> neighbors = new();

			if (_mapManager == null)
				return neighbors;

			foreach (var dir in _hexNeighbors)
			{
				Vector2I cell = _mapManager.GetNeighborCell(_currentGridPos, dir);

				if (_mapManager.IsTileWalkable(cell) &&
					!_mapManager.IsTileOccupied(cell))
				{
					neighbors.Add(cell);
				}
			}

			return neighbors;
		}

		private Vector2I GetBestMoveTowards(Vector2 targetWorldPos)
		{
			var neighbors = GetValidNeighbors();

			if (neighbors.Count == 0)
				return _currentGridPos;

			Vector2I best = neighbors[0];
			float bestDist = float.MaxValue;

			foreach (var cell in neighbors)
			{
				Vector2 worldPos = _mapManager.MapToLocal(cell);
				float dist = worldPos.DistanceTo(targetWorldPos);

				if (dist < bestDist)
				{
					bestDist = dist;
					best = cell;
				}
			}

			return best;
		}

		private async Task MoveTowardsPlayer()
		{
			Vector2I next = GetBestMoveTowards(_targetPlayer.GlobalPosition);

			if (next != _currentGridPos)
				await MoveToGrid(next);
		}

		private async Task MoveAwayFromPlayer()
		{
			Vector2 awayDir =
				GlobalPosition + (GlobalPosition - _targetPlayer.GlobalPosition);

			Vector2I next = GetBestMoveTowards(awayDir);

			if (next != _currentGridPos)
				await MoveToGrid(next);
		}


		private async Task Wait(float time = 0.3f)
		{
			await ToSignal(GetTree().CreateTimer(time), "timeout");
		}

		private async Task MoveByDirection(Vector2 dir)
		{
			Vector2I best = _currentGridPos;
			float bestScore = -9999f;

			foreach (var n in _hexNeighbors)
			{
				Vector2I cell = _mapManager.GetNeighborCell(_currentGridPos, n);
				if (!_mapManager.IsTileWalkable(cell)) continue;
				if (_mapManager.IsTileOccupied(cell)) continue;

				Vector2 world = _mapManager.GridToWorld(cell);
				float score = world.DirectionTo(GlobalPosition + dir).Dot(dir);

				if (score > bestScore)
				{
					bestScore = score;
					best = cell;
				}
			}

			if (best != _currentGridPos)
				await MoveToGrid(best);
		}

		private async Task MoveToGrid(Vector2I target)
		{
			Vector2 world = _mapManager.GridToWorld(target);
			Tween t = CreateTween();
			t.TweenProperty(this, "global_position", world, MoveDuration);
			await ToSignal(t, "finished");
			_currentGridPos = target;
		}
		private int GetGridDistanceToPlayer()
		{
			Vector2I playerGrid = _mapManager.GetGridCoordinates(_targetPlayer.GlobalPosition);
			return (int)_currentGridPos.DistanceTo(playerGrid);
		}

		/* =======================
		 * DAMAGE & DEATH
		 * ======================= */
		public void TakeDamage(int dmg)
		{
			_currentHP -= dmg;
			ShowDamagePopup(dmg);
			if (_healthBar != null) _healthBar.Value = _currentHP;

			if (_currentHP <= 0)
				Die();
		}

		private void ShowDamagePopup(int amount)
		{
			if (DamagePopupScene == null) return;

			var popup = DamagePopupScene.Instantiate<DamagePopup>();
			AddChild(popup);

			// Enemy kena damage → KUNING
			popup.SetupAndAnimate(
				amount,
				GlobalPosition + new Vector2(0, -30),
				Colors.Yellow
			);
		}

		private async void Die()
		{
			Tween t = CreateTween();
			t.TweenProperty(this, "modulate:a", 0f, 0.4f);
			t.TweenProperty(this, "scale", Vector2.Zero, 0.4f);
			await ToSignal(t, "finished");
			QueueFree();
		}
	}
}
