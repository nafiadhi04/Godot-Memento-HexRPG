using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks; // Wajib untuk async/await
using MementoTest.Entities;   // Agar kenal PlayerController
using MementoTest.Resources;  // Agar kenal EnemySkill
using MementoTest.UI;         // Agar kenal BattleHUD
using MementoTest.Core;       // Agar kenal ScoreManager

namespace MementoTest.Entities
{
	public partial class EnemyController : CharacterBody2D
	{
		// --- AI SETTINGS ---
		public enum AIBehavior { Aggressive, Kiting }

		[ExportGroup("AI Behavior")]
		[Export] public AIBehavior BehaviorType = AIBehavior.Aggressive; // Pilih di Inspector
		[Export] public float AttackRange = 60f;        // Jarak serang melee
		[Export] public float KiteSafeDistance = 180f;  // Jarak aman untuk tipe Kiting
		[Export] public float MoveDuration = 0.3f;

		[ExportGroup("Stats")]
		[Export] public int MaxHP = 50;

		[ExportGroup("Skills & Combat")]
		[Export] public Godot.Collections.Array<EnemySkill> SkillList; // Skill yang bisa dipakai
		[Export] public PackedScene DamagePopupScene;
		[Export] public float ReactionTimeMelee = 1.5f;
		[Export] public float ReactionTimeRanged = 2.0f;

		// --- INTERNAL VARIABLES ---
		private int _currentHP;
		private PlayerController _targetPlayer;
		private MapManager _mapManager;
		private Vector2I _currentGridPos;
		private bool _isBusy = false;
		private bool _isMoving = false;
		private Random _rng = new Random();
		private ProgressBar _healthBar;
		private BattleHUD _hud;

		private readonly TileSet.CellNeighbor[] _hexNeighbors = {
			TileSet.CellNeighbor.TopSide,
			TileSet.CellNeighbor.BottomSide,
			TileSet.CellNeighbor.TopLeftSide,
			TileSet.CellNeighbor.TopRightSide,
			TileSet.CellNeighbor.BottomLeftSide,
			TileSet.CellNeighbor.BottomRightSide
		};

		public override void _Ready()
		{
			base._Ready();
			_currentHP = MaxHP;

			// Setup HealthBar
			_healthBar = GetNodeOrNull<ProgressBar>("HealthBar");
			if (_healthBar != null)
			{
				_healthBar.MaxValue = MaxHP;
				_healthBar.Value = _currentHP;
				_healthBar.Visible = true;
			}

			// Setup MapManager
			if (GetParent().HasNode("MapManager"))
			{
				_mapManager = GetParent().GetNode<MapManager>("MapManager");
				_currentGridPos = _mapManager.GetGridCoordinates(GlobalPosition);
				GlobalPosition = _mapManager.GetSnappedWorldPosition(GlobalPosition);
			}

			AddToGroup("Enemy");

			// [FIX] Cari Player dengan Group
			var playerNode = GetTree().GetFirstNodeInGroup("Player");
			if (playerNode is PlayerController player)
			{
				_targetPlayer = player;
			}

			// [FIX UTAMA] Cari BattleHUD dengan Group "HUD"
			// Pastikan node BattleHUD di scene sudah dimasukkan ke Group "HUD"
			var hudNode = GetTree().GetFirstNodeInGroup("HUD");
			if (hudNode is MementoTest.UI.BattleHUD hud)
			{
				_hud = hud;
				// GD.Print($"[ENEMY] {Name} connected to HUD.");
			}
			else
			{
				GD.PrintErr($"[ENEMY] {Name} GAGAL connect ke BattleHUD! Pastikan HUD ada di group 'HUD'.");
			}

			// Fallback Skill jika kosong
			if (SkillList == null || SkillList.Count == 0)
			{
				// [FIX] Gunakan Object Initializer agar tidak butuh Constructor khusus
				// Ini lebih aman untuk Resource Godot
				var defaultSkill = new EnemySkill
				{
					SkillName = "Punch",
					Damage = 5,
					AttackRange = 60f
				};

				SkillList = new Godot.Collections.Array<EnemySkill> { defaultSkill };
			}

			FindTargetPlayer();
			SnapToNearestGrid();
		}

		private void SnapToNearestGrid()
		{
			if (MapManager.Instance == null) return;
			Vector2I gridCoords = MapManager.Instance.WorldToGrid(GlobalPosition);
			GlobalPosition = MapManager.Instance.GridToWorld(gridCoords);
		}

		private void FindTargetPlayer()
		{
			// Target Selection Logic (Sesuai Dokumen Poin 5)
			var playerNode = GetTree().GetFirstNodeInGroup("Player");
			if (playerNode is PlayerController player)
			{
				_targetPlayer = player;
			}
		}

		// --- LOGIKA UTAMA TURN (AI BRAIN) ---
		public async Task ExecuteTurn()
		{
			if (_isBusy) return;
			_isBusy = true;

			try
			{
				// ... (Logika Cari Target sama seperti sebelumnya) ...
				if (_targetPlayer == null || !GodotObject.IsInstanceValid(_targetPlayer))
				{
					FindTargetPlayer();
					if (_targetPlayer == null) { await ToSignal(GetTree().CreateTimer(0.5f), "timeout"); return; }
				}

				float distToPlayer = GlobalPosition.DistanceTo(_targetPlayer.GlobalPosition);
				GD.Print($"[AI] Jarak ke Player: {distToPlayer:F1}"); // DEBUG JARAK

				// 1. MOVEMENT LOGIC
				Vector2I targetGrid = _currentGridPos;
				bool shouldMove = false;

				if (BehaviorType == AIBehavior.Aggressive)
				{
					// [REVISI] Hanya maju jika jarak > AttackRange (dikurangi toleransi biar nempel)
					if (distToPlayer > AttackRange - 5f)
					{
						targetGrid = GetBestMoveTowards(_targetPlayer.GlobalPosition);
						shouldMove = true;
					}
				}
				else if (BehaviorType == AIBehavior.Kiting)
				{
					// ... (Logika Kiting sama) ...
					if (distToPlayer < KiteSafeDistance) { /* Kabur */ targetGrid = GetBestMoveTowards(GlobalPosition + (GlobalPosition - _targetPlayer.GlobalPosition)); shouldMove = true; }
					else if (distToPlayer > AttackRange * 1.5f) { /* Maju */ targetGrid = GetBestMoveTowards(_targetPlayer.GlobalPosition); shouldMove = true; }
				}

				// Eksekusi Gerak
				if (shouldMove && targetGrid != _currentGridPos)
				{
					await MoveToGrid(targetGrid);
					// Update jarak setelah gerak
					distToPlayer = GlobalPosition.DistanceTo(_targetPlayer.GlobalPosition);
				}

				// 2. ATTACK LOGIC
				// Filter Skill yang NYAMPAI
				var validSkills = SkillList.Where(s => s.AttackRange >= distToPlayer).ToList();

				GD.Print($"[AI] Valid Skills: {validSkills.Count} (Total Skill: {SkillList.Count})");

				if (validSkills.Count > 0)
				{
					int index = _rng.Next(validSkills.Count);
					await PerformSkill(validSkills[index]);
				}
				else
				{
					// [SOLUSI MACET]
					// Jika sudah dekat (Aggressive) tapi gak ada skill yang nyampai (karena settingan salah),
					// Kita paksa pakai skill pertama (jika ada) daripada game macet.
					if (BehaviorType == AIBehavior.Aggressive && SkillList.Count > 0 && distToPlayer <= AttackRange + 20f)
					{
						GD.Print("[AI WARNING] Jarak skill kurang, tapi memaksa serang!");
						await PerformSkill(SkillList[0]);
					}
					else
					{
						GD.Print("[AI] Tidak bisa menyerang (Target terlalu jauh/Skill Range kependekan). Passing Turn.");
						await ToSignal(GetTree().CreateTimer(0.3f), "timeout");
					}
				}
			}
			finally
			{
				_isBusy = false;
			}
		}

		// Helper: Mencari Tile Tetangga Terbaik untuk menuju Target
		private Vector2I GetBestMoveTowards(Vector2 targetPos)
		{
			List<Vector2I> validMoves = GetValidNeighbors();
			if (validMoves.Count == 0) return _currentGridPos;

			// Urutkan move berdasarkan jarak terdekat ke targetPos
			return validMoves.OrderBy(pos => _mapManager.MapToLocal(pos).DistanceTo(targetPos)).First();
		}

		private List<Vector2I> GetValidNeighbors()
		{
			List<Vector2I> neighbors = new List<Vector2I>();
			foreach (var direction in _hexNeighbors)
			{
				Vector2I neighborCell = _mapManager.GetNeighborCell(_currentGridPos, direction);
				if (_mapManager.IsTileWalkable(neighborCell) && !_mapManager.IsTileOccupied(neighborCell))
				{
					neighbors.Add(neighborCell);
				}
			}
			return neighbors;
		}

		private async Task MoveToGrid(Vector2I targetGrid)
		{
			_isMoving = true;
			Vector2 targetWorldPos = _mapManager.MapToLocal(targetGrid);

			Tween tween = CreateTween();
			tween.TweenProperty(this, "global_position", targetWorldPos, MoveDuration)
				.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);

			await ToSignal(tween, "finished");
			_currentGridPos = targetGrid;
			_isMoving = false;
		}

		// --- COMBAT EXECUTION ---
		private async Task PerformSkill(EnemySkill skill)
		{
			GD.Print($"{Name} using skill [{skill.SkillName}]");

			Vector2 originalPos = GlobalPosition;
			Vector2 direction = originalPos.DirectionTo(_targetPlayer.GlobalPosition);

			// Animasi Maju Sedikit (Anticipation)
			Vector2 attackPos = originalPos + (direction * 20f);
			Tween attackTween = CreateTween();
			attackTween.TweenProperty(this, "global_position", attackPos, 0.2f);
			await ToSignal(attackTween, "finished");

			// --- REACTION PHASE ---
			bool isDodged = false;
			if (_hud != null)
			{
				// Tentukan kata kunci & waktu
				string word = (skill.AttackRange < 150) ? "parry" : "dodge";
				float time = (skill.AttackRange < 150) ? ReactionTimeMelee : ReactionTimeRanged;

				// [TASK BASED WAIT] Mencegah macet
				isDodged = await _hud.WaitForPlayerReaction(word, time);
			}

			// --- DAMAGE CALCULATION ---
			if (isDodged)
			{
				GD.Print(">>> REACTION SUCCESS! 0 DAMAGE.");
				_targetPlayer.TakeDamage(0); // Miss

				// [SCORING] Tambah skor sedikit karena berhasil menghindar
				ScoreManager.Instance?.AddScore(50);
			}
			else
			{
				GD.Print(">>> FAILED! TAKING DAMAGE.");
				_targetPlayer.TakeDamage(skill.Damage);

				// [SCORING] Kena Hit = Reset Combo!
				ScoreManager.Instance?.ResetCombo();
			}

			// Animasi Mundur
			Tween returnTween = CreateTween();
			returnTween.TweenProperty(this, "global_position", originalPos, 0.2f);
			await ToSignal(returnTween, "finished");
		}

		// --- HEALTH & DEATH ---
		public void TakeDamage(int damage)
		{
			_currentHP -= damage;
			if (_healthBar != null) _healthBar.Value = _currentHP;

			ShowDamagePopup(damage);

			// Efek Hit (Flash Red)
			Modulate = Colors.Red;
			CreateTween().TweenProperty(this, "modulate", Colors.White, 0.2f);

			if (_currentHP <= 0) Die();
		}

		private void ShowDamagePopup(int amount)
		{
			if (DamagePopupScene != null)
			{
				var popup = DamagePopupScene.Instantiate<DamagePopup>();
				AddChild(popup);
				popup.SetupAndAnimate(amount, GlobalPosition + new Vector2(0, -30), Colors.Yellow);
			}
		}

		private async void Die()
		{
			GD.Print($"ENEMY DEFEATED: {Name}");

			// Matikan collision
			var collision = GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
			if (collision != null) collision.SetDeferred("disabled", true);
			if (_healthBar != null) _healthBar.Visible = false;

			// Animasi Mati
			Tween tween = CreateTween();
			tween.SetParallel(true);
			tween.TweenProperty(this, "modulate:a", 0f, 0.5f);
			tween.TweenProperty(this, "scale", Vector2.Zero, 0.5f);
			tween.TweenProperty(this, "rotation_degrees", 360f, 0.5f);

			await ToSignal(tween, "finished");
			QueueFree();
		}
	}
}