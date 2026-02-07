using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MementoTest.UI;
using MementoTest.Core;
using MementoTest.Resources;
using System.Linq;

namespace MementoTest.Entities
{
	public partial class PlayerController : CharacterBody2D
	{
		[ExportCategory("Player Stats")]
		[Export] public int MaxHP = 100;
		[Export] public int MaxAP = 10;
		[Export] public int TypoPenaltyAP = 2;
		[Export] public int SuccessRegenAP = 3;
		[Export] public int TurnStartBaseRegen = 2;

		[ExportCategory("Combat Data")]
		[Export] public Godot.Collections.Array<PlayerSkill> SkillList;
		[Export] public PackedScene DamagePopupScene;

		[Export] public AnimatedSprite2D Sprite;


		// State Variables
		private int _currentHP;
		public int CurrentHP => _currentHP;

		private int _currentAP;
		private bool _isMoving = false;
		private bool _isAttacking = false;
		private Vector2I _currentGridPos;

		// References
		private MapManager _mapManager;
		private TurnManager _turnManager;
		private BattleHUD _hud;
		private EnemyController _targetEnemy;
		private Tween _activeTween;
		private Dictionary<string, (int Cost, int Damage)> _skillDatabase;

		public override void _Ready()
		{
			base._Ready();

			// [FIX VISUAL] Paksa Player selalu di atas lantai & selector
			ZIndex = 5;

			AddToGroup("Player");

			// Setup Database Skill
			_skillDatabase = new Dictionary<string, (int, int)>();
			if (SkillList != null)
			{
				foreach (var skill in SkillList)
				{
					_skillDatabase[skill.CommandName.ToLower()] = (skill.ApCost, skill.Damage);
				}
			}

			_currentHP = MaxHP;
			_currentAP = MaxAP;

			// Setup References
			if (GetParent().HasNode("MapManager"))
				_mapManager = GetParent().GetNode<MapManager>("MapManager");

			if (GetParent().HasNode("TurnManager"))
			{
				_turnManager = GetParent().GetNode<TurnManager>("TurnManager");
				_turnManager.PlayerTurnStarted += OnPlayerTurnStart;
			}

			if (GetParent().HasNode("BattleHUD"))
			{
				_hud = GetParent().GetNode<BattleHUD>("BattleHUD");
				_hud.CommandSubmitted += ExecuteCombatCommand;
				_hud.UpdateAP(_currentAP, MaxAP);
			}

			// Snap posisi awal (Tunggu 1 frame biar aman)
			CallDeferred(nameof(SnapToNearestGrid));
		}

		public override void _Process(double delta)
		{
			if (_isMoving) return;

			Vector2 dir = (GetGlobalMousePosition() - GlobalPosition).Normalized();
			string suffix = GetDirectionSuffix(dir);

			string anim = $"idle_{suffix}";
			if (Sprite.Animation != anim)
				Sprite.Play(anim);
		}

		private void SnapToNearestGrid()
		{
			if (MapManager.Instance == null) return;

			// Update posisi Grid & World
			_currentGridPos = MapManager.Instance.WorldToGrid(GlobalPosition);
			GlobalPosition = MapManager.Instance.GridToWorld(_currentGridPos);

			GD.Print($"[PLAYER] Ready at Grid: {_currentGridPos}");
		}

		// --- INPUT HANDLING (SATPAM) ---
		public override void _Input(InputEvent @event)
		{
			// 1. Cek Mouse Klik Kiri
			if (@event is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
			{
				// [PENTING] BLOK INPUT JIKA:
				// - Bukan giliran Player
				// - Sedang Animasi Jalan
				// - Sedang Animasi Serang
				// - Player Mati
				if (_turnManager?.CurrentTurn != TurnManager.TurnState.Player) return;
				if (_isMoving || _isAttacking) return;
				if (_currentHP <= 0) return;

				CheckEnemyClick();
			}
		}
		public IEnumerable<PlayerSkill> GetAvailableSkills()
		{

			return SkillList;
		}

		public IEnumerable<string> GetAvailableSkillCommands()
		{
			return SkillList
				.Where(skill => skill != null)
				.Select(skill => skill.CommandName.ToLower());
		}

		private string GetDirectionSuffix(Vector2 dir)
		{
			if (dir.Y < -0.5f)
			{
				if (dir.X < -0.5f) return "up_left";
				if (dir.X > 0.5f) return "up_right";
				return "up";
			}
			else if (dir.Y > 0.5f)
			{
				if (dir.X < -0.5f) return "down_left";
				if (dir.X > 0.5f) return "down_right";
				return "down";
			}
			else
			{
				if (dir.X < 0) return "left";
				return "right";
			}
		}

		private void CheckEnemyClick()
		{
			Vector2 mousePos = GetGlobalMousePosition();
			var spaceState = GetWorld2D().DirectSpaceState;

			var query = new PhysicsPointQueryParameters2D
			{
				Position = mousePos,
				CollideWithBodies = true,
				CollideWithAreas = true
			};

			var result = spaceState.IntersectPoint(query);

			if (result.Count > 0)
			{
				Node collider = (Node)result[0]["collider"];

				// Jika Klik Musuh
				if (collider is EnemyController enemy)
				{
					// Cek jarak dulu (Opsional: Kalau mau melee only)
					// if (GlobalPosition.DistanceTo(enemy.GlobalPosition) > 100) { ... }

					_targetEnemy = enemy;
					_hud?.ShowCombatPanel(true);
					_hud?.LogToTerminal($"> TARGET LOCKED: {enemy.Name}", Colors.Yellow);
					return;
				}
			}

			// Jika Klik Tanah Kosong -> Jalan
			_targetEnemy = null;
			_hud?.ShowCombatPanel(false);
			TryMoveToTile(mousePos);
		}

		// --- MOVEMENT LOGIC ---
		private async void TryMoveToTile(Vector2 mousePos)
		{
			if (_mapManager == null) return;

			Vector2I targetGrid = _mapManager.GetGridCoordinates(mousePos);

			// Validasi Gerak
			if (!_mapManager.IsNeighbor(_currentGridPos, targetGrid)) return; // Kejauhan
			if (!_mapManager.IsTileWalkable(targetGrid)) return; // Tembok/Air
			if (_mapManager.IsTileOccupied(targetGrid)) return; // Ada unit lain

			// Cek AP
			int moveCost = 1;
			if (_currentAP >= moveCost)
			{
				_currentAP -= moveCost;
				_hud?.UpdateAP(_currentAP, MaxAP);

				await MoveToGrid(targetGrid);
			}
			else
			{
				_hud?.LogToTerminal("ERROR: NOT ENOUGH AP!", Colors.Red);
			}
		}

		private async Task MoveToGrid(Vector2I targetGrid)
		{
			if (_isMoving) return;
			_isMoving = true;

			Vector2 targetWorldPos = MapManager.Instance.GridToWorld(targetGrid);
			Vector2 dir = (targetWorldPos - GlobalPosition).Normalized();


			string suffix = GetDirectionSuffix(dir);
			Sprite.Play($"walk_{suffix}");

			// Setup Tween
			if (_activeTween != null && _activeTween.IsValid()) _activeTween.Kill();

			_activeTween = CreateTween();

			// Tween ke Global Position yang sudah benar
			_activeTween.TweenProperty(this, "global_position", targetWorldPos, 0.3f)
						.SetTrans(Tween.TransitionType.Sine)
						.SetEase(Tween.EaseType.Out);

			await ToSignal(_activeTween, "finished");
			Sprite.Play($"idle_{suffix}");
			_isMoving = false;

			_currentGridPos = targetGrid;
			_isMoving = false;

			// [SAFETY CHECK]
			// Kadang animasi meleset 0.001 pixel, kita paksa snap lagi di akhir gerakan biar sempurna
			GlobalPosition = targetWorldPos;
		}

		// --- COMBAT LOGIC ---
		private async void ExecuteCombatCommand(string command)
		{
			// Validasi Target
			if (_targetEnemy == null || !GodotObject.IsInstanceValid(_targetEnemy))
			{
				_hud?.LogToTerminal("ERROR: NO VALID TARGET.", Colors.Red);
				return;
			}

			// Validasi Giliran (Double check)
			if (_turnManager.CurrentTurn != TurnManager.TurnState.Player) return;

			command = command.ToLower().Trim();

			// SKENARIO SUKSES (Skill Ditemukan)
			if (_skillDatabase.ContainsKey(command))
			{
				var skill = _skillDatabase[command];

				if (_currentAP >= skill.Cost)
				{
					// 1. Kurangi AP & Regen
					_currentAP -= skill.Cost;
					_currentAP = Math.Min(_currentAP + SuccessRegenAP, MaxAP); // Regen + Cap Max
					_hud?.UpdateAP(_currentAP, MaxAP);

					_hud?.LogToTerminal($"> EXECUTING '{command.ToUpper()}'...", Colors.Green);

					// 2. [SCORING] Tambah Skor & Combo!
					// Skor dasar 100 per serangan sukses (bisa diubah sesuai damage skill)
					ScoreManager.Instance?.AddScore(100);

					// 3. Eksekusi Serangan (Animasi & Damage)
					await PerformAttackAnimation(skill.Damage);
				}
				else
				{
					_hud?.LogToTerminal($"ERROR: NEED {skill.Cost} AP.", Colors.Red);
					// (Opsional) Tidak reset combo kalau cuma kurang AP, tapi terserah desain game-mu
				}
			}
			// SKENARIO TYPO / GAGAL (Skill Tidak Ditemukan)
			else
			{
				// 1. Hukuman AP
				_currentAP = Math.Max(0, _currentAP - TypoPenaltyAP);
				_hud?.UpdateAP(_currentAP, MaxAP);

				// 2. [SCORING] Reset Combo karena Typo!
				ScoreManager.Instance?.ResetCombo();

				_hud?.LogToTerminal($"> SYNTAX ERROR: '{command}'", Colors.Red);

				await EndCombatSession("SYSTEM HALTED.");
			}
		}

		private async Task PerformAttackAnimation(int damage)
		{
			_isAttacking = true;

			Vector2 originalPos = GlobalPosition;
			Vector2 targetPos = _targetEnemy.GlobalPosition;
			Vector2 direction = (targetPos - originalPos).Normalized();
			Vector2 lungePos = originalPos + (direction * 40f); // Maju sedikit

			// Animasi Maju Pukul
			if (_activeTween != null && _activeTween.IsValid()) _activeTween.Kill();
			_activeTween = CreateTween();

			// 1. Mundur dikit (ancang-ancang)
			_activeTween.TweenProperty(this, "global_position", originalPos - (direction * 10f), 0.1f);

			// 2. Maju Cepat (Hit)
			_activeTween.TweenProperty(this, "global_position", lungePos, 0.1f)
						.SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);

			await ToSignal(_activeTween, "finished");

			// IMPACT
			if (GodotObject.IsInstanceValid(_targetEnemy))
			{
				_targetEnemy.TakeDamage(damage);
			}

			// 3. Kembali ke posisi asal
			_activeTween = CreateTween();
			_activeTween.TweenProperty(this, "global_position", originalPos, 0.2f);

			await ToSignal(_activeTween, "finished");

			_isAttacking = false;
			await EndCombatSession("TARGET HIT.");
		}

		private async Task EndCombatSession(string message)
		{
			_hud?.LogToTerminal($"> {message}", Colors.Gray);
			await ToSignal(GetTree().CreateTimer(1.0f), "timeout"); // Delay biar kebaca

			_hud?.ShowCombatPanel(false);

			// Akhiri Giliran Player
			_turnManager?.ForceEndPlayerTurn();
		}

		// --- TURN & STATUS ---

		private void OnPlayerTurnStart()
		{
			_currentAP = Math.Min(_currentAP + TurnStartBaseRegen, MaxAP);
			_hud?.UpdateAP(_currentAP, MaxAP);
			_hud?.LogToTerminal($"--- AP RECHARGED (+{TurnStartBaseRegen}) ---", Colors.Cyan);

			// Re-open combat panel kalau masih ada target
			if (_targetEnemy != null && GodotObject.IsInstanceValid(_targetEnemy))
			{
				// Jika masih ada musuh -> Buka Panel Combat & Ambil Fokus Keyboard
				if (_hud != null)
				{
					_hud.ShowCombatPanel(true);

					// [FIX BUG ENTER] Panggil fungsi yang baru kita buat
					_hud.EnablePlayerInput();
				}
			}
			else
			{
				// Jika tidak ada musuh -> Tutup Panel
				_targetEnemy = null;
				if (_hud != null) _hud.ShowCombatPanel(false);
			}
		}

		public void TakeDamage(int damage)
		{
			_currentHP -= damage;
			ShowDamagePopup(damage);

			_hud?.UpdateHP(_currentHP, MaxHP);

			// Efek Merah
			Modulate = Colors.Red;
			CreateTween().TweenProperty(this, "modulate", Colors.White, 0.3f);

			if (damage > 0)
			{
				ScoreManager.Instance.ResetCombo();
			}

			if (_currentHP <= 0) Die();
		}

		private void ShowDamagePopup(int amount)
		{
			if (DamagePopupScene == null) return;

			var popup = DamagePopupScene.Instantiate<DamagePopup>();
			AddChild(popup);

			popup.SetupAndAnimate(
				amount,
				GlobalPosition + new Vector2(0, -30),
				Colors.Red
			);
		}

		private void Die()
		{
			GD.Print("GAME OVER");
			SetPhysicsProcess(false);
			SetProcessInput(false); // Matikan input
		}

		
	}
	
	
}