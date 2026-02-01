using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks; // Wajib untuk async/await
using MementoTest.UI;
using MementoTest.Core;
using MementoTest.Resources;

namespace MementoTest.Entities
{
	public partial class PlayerController : CharacterBody2D
	{
		[Export] public int MaxHP = 100;
		[Export] public int MaxAP = 10;
		[Export] public int TypoPenaltyAP = 2;
		[Export] public Godot.Collections.Array<PlayerSkill> SkillList;
		[Export] public PackedScene DamagePopupScene;
		[Export] public int SuccessRegenAP = 3; // Berapa AP yang didapat kalau sukses ngetik
		[Export] public int TurnStartBaseRegen = 1; // Modal AP tiap ganti giliran (biar gak macet total)


		private int _currentHP;
		private MapManager _mapManager;
		private MementoTest.Core.TurnManager _turnManager;
		private int _currentAP;
		private bool _isMoving = false;
		private Vector2I _currentGridPos;

		// Kita tetap pakai Dictionary ini untuk pencarian cepat (internal)
		private Dictionary<string, (int Cost, int Damage)> _skillDatabase;

		private BattleHUD _hud;
		private EnemyController _targetEnemy;
		private bool _isAttacking = false;
		private Tween _activeTween;

		public override void _Ready()
		{
			base._Ready();

			// --- BAGIAN BARU: KONVERSI INSPECTOR KE DICTIONARY ---
			_skillDatabase = new Dictionary<string, (int, int)>();

			if (SkillList != null)
			{
				foreach (var skill in SkillList)
				{
					// Masukkan data dari Inspector ke Dictionary
					// Kita pakai ToLower() agar player bisa ngetik huruf besar/kecil bebas
					_skillDatabase[skill.CommandName.ToLower()] = (skill.ApCost, skill.Damage);
				}
			}

			_currentHP = MaxHP;
			GD.Print($"Player HP Initialized: {_currentHP}/{MaxHP}");
			// -----------------------------------------------------

			// Setup lainnya tetap sama...
			if (GetParent().HasNode("MapManager"))
			{
				_mapManager = GetParent().GetNode<MapManager>("MapManager");
				_currentGridPos = _mapManager.GetGridCoordinates(GlobalPosition);
				GlobalPosition = _mapManager.GetSnappedWorldPosition(GlobalPosition);
			}

			if (GetParent().HasNode("TurnManager"))
			{
				_turnManager = GetParent().GetNode<MementoTest.Core.TurnManager>("TurnManager");
				_turnManager.PlayerTurnStarted += OnPlayerTurnStart;
			}

			_currentAP = MaxAP;

			if (GetParent().HasNode("BattleHUD"))
			{
				_hud = GetParent().GetNode<BattleHUD>("BattleHUD");
				_hud.CommandSubmitted += ExecuteCombatCommand;
				_hud.UpdateAP(_currentAP, MaxAP);
			}
		}
		// Logic mengecek apakah boleh jalan ke sana?
		private async void TryMoveToTile(Vector2 mousePos)
		{
			// Cegah gerak kalau sedang animasi jalan
			if (_isMoving) return;

			Vector2I targetGrid = _mapManager.GetGridCoordinates(mousePos);

			// Aturan 1: Hanya boleh jalan ke tetangga (jarak 1 hex)
			if (!_mapManager.IsNeighbor(_currentGridPos, targetGrid))
			{
				GD.Print("Terlalu jauh!");
				return;
			}

			// Aturan 2: Cek apakah tile bisa diinjak (Air/Gunung)
			if (!_mapManager.IsTileWalkable(targetGrid)) return;

			// Aturan 3: Cek apakah ada unit lain di sana
			if (_mapManager.IsTileOccupied(targetGrid)) return;

			// Aturan 4: Cek AP (Biaya Jalan = 1 AP)
			int moveCost = 1;
			if (_currentAP >= moveCost)
			{
				// Kurangi AP
				_currentAP -= moveCost;
				if (_hud != null) _hud.UpdateAP(_currentAP, MaxAP);

				// Lakukan Gerakan
				await MoveToGrid(targetGrid);
			}
			else
			{
				// Beri tahu player AP habis
				if (_hud != null) _hud.LogToTerminal("ERROR: NOT ENOUGH AP TO MOVE!", Colors.Red);
			}
		}

		// Logic Animasi Gerak (Tween)
		private async System.Threading.Tasks.Task MoveToGrid(Vector2I targetGrid)
		{
			_isMoving = true;
			Vector2 targetWorldPos = _mapManager.MapToLocal(targetGrid);

			// Update bagian ini:
			if (_activeTween != null && _activeTween.IsValid()) _activeTween.Kill();
			_activeTween = CreateTween();

			_activeTween.TweenProperty(this, "global_position", targetWorldPos, 0.3f);

			await ToSignal(_activeTween, "finished");

			_currentGridPos = targetGrid;
			_isMoving = false;
		}
		private void OnPlayerTurnStart()
		{
			// [GANTI DENGAN LOGIKA BARU]
			// 1. Tambah sedikit modal (Base Regen)
			_currentAP += TurnStartBaseRegen;

			// 2. Pastikan tidak melebihi batas MaxAP
			if (_currentAP > MaxAP) _currentAP = MaxAP;

			// 3. Update UI
			if (_hud != null)
			{
				_hud.UpdateAP(_currentAP, MaxAP);
				// Beri tahu player status AP-nya
				_hud.LogToTerminal($"--- SYSTEM REBOOTED. AP +{TurnStartBaseRegen} ---", Colors.Cyan);
			}

			// ... (Logika Auto-Reconnect target musuh tetap sama) ...
			if (_targetEnemy != null && GodotObject.IsInstanceValid(_targetEnemy))
			{
				_hud.ShowCombatPanel(true);
				// ...
			}
		}

		// Logic Input Mouse untuk Memilih Target
		public override void _Input(InputEvent @event)
		{
			if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Left)
			{
				// Cek apakah kita klik musuh?
				CheckEnemyClick();
			}
		}

		private void CheckEnemyClick()
		{
			Vector2 mousePos = GetGlobalMousePosition();
			var spaceState = GetWorld2D().DirectSpaceState;

			var query = new PhysicsPointQueryParameters2D();
			query.Position = mousePos;
			query.CollideWithBodies = true;
			query.CollideWithAreas = true;

			var result = spaceState.IntersectPoint(query);

			if (result.Count > 0)
			{
				Node collider = (Node)result[0]["collider"];
				if (collider is EnemyController enemy)
				{
					// KENA MUSUH -> Mode Combat
					_targetEnemy = enemy;
					_hud.ShowCombatPanel(true);
					_hud.LogToTerminal($"> TARGET LOCKED: {enemy.Name}", Colors.Yellow);
				}
			}
			else
			{
				// KENA TANAH KOSONG -> Mode Jalan
				_targetEnemy = null;
				if (_hud != null) _hud.ShowCombatPanel(false);

				// --- TAMBAHAN PENTING: PANGGIL FUNGSI GERAK ---
				TryMoveToTile(mousePos);
			}
		}

		// FUNGSI UTAMA: Menerima teks dari UI dan memprosesnya
		private async void ExecuteCombatCommand(string command)
		{
			// ... (pengecekan target awal tetap sama) ...
			if (_targetEnemy == null || !GodotObject.IsInstanceValid(_targetEnemy)) return;

			command = command.ToLower().Trim();

			// SKENARIO 1: Command Ditemukan (Sukses)
			if (_skillDatabase.ContainsKey(command))
			{
				var skill = _skillDatabase[command];
				int apCost = skill.Cost;
				int damage = skill.Damage;

				if (_currentAP >= apCost)
				{
					_currentAP -= apCost;

					_currentAP += SuccessRegenAP;
					if (_currentAP > MaxAP) _currentAP = MaxAP;

					// 2. [WAJIB ADA] Update UI agar bar AP berubah
					if (_hud != null) _hud.UpdateAP(_currentAP, MaxAP);

					_hud.LogToTerminal($"> EXECUTING '{command}'...", Colors.Green);


					// [FIX 1] Matikan animasi jalan/gerak sebelumnya jika ada
					if (_activeTween != null && _activeTween.IsValid())
					{
						_activeTween.Kill();
						_isMoving = false; // Reset status moving
					}

					_isAttacking = true;

					// [FIX 2] Debug Koordinat - Cek Output Panel nanti!
					Vector2 originalPos = GlobalPosition;
					Vector2 direction = GlobalPosition.DirectionTo(_targetEnemy.GlobalPosition);

					float lungeDistance = 25f;

					// Kita perbesar jaraknya jadi 60f biar kelihatan jelas
					Vector2 attackPos = originalPos + (direction * lungeDistance);

					GD.Print($"[DEBUG ANIM] Start: {originalPos} | Target: {attackPos} | Dist: {originalPos.DistanceTo(attackPos)}");

					// [FIX 3] Simpan ke variabel _activeTween
					float lungeDuration = 0.25f; // Sebelumnya 0.1f (Terlalu cepat)
					float returnDuration = 0.2f; // Sebelumnya 0.15f

					_activeTween = CreateTween();

					// 1. MAJU (Lunge) - Lebih pelan tapi tetap bertenaga (TransitionType.Back)
					_activeTween.TweenProperty(this, "global_position", attackPos, lungeDuration)
						 .SetTrans(Tween.TransitionType.Back) // Efek ancang-ancang mundur dikit lalu maju
						.SetEase(Tween.EaseType.Out);

					// 2. MUNDUR (Return) - Rileks
					_activeTween.TweenProperty(this, "global_position", originalPos, returnDuration)
						 .SetTrans(Tween.TransitionType.Quad) // Gerakan melambat saat sampai
						.SetEase(Tween.EaseType.Out);

					// TUNGGU IMPACT
					// Kita tunggu tepat saat animasi maju selesai (0.25 detik)
					await ToSignal(GetTree().CreateTimer(lungeDuration), "timeout");

					// --- MOMEN IMPACT (Tabrakan) ---
					if (GodotObject.IsInstanceValid(_targetEnemy))
					{
						// Masukkan damage pas di sini
						_targetEnemy.TakeDamage(damage);
					}

					// Tambahkan "Hang Time" (Diam sebentar 0.1 detik biar berasa nabraknya)
					// Baru setelah itu biarkan animasi mundur berjalan
					await ToSignal(GetTree().CreateTimer(0.1f), "timeout");

					// Tunggu sampai seluruh rangkaian tween selesai
					if (_activeTween != null && _activeTween.IsValid())
					{
						await ToSignal(_activeTween, "finished");
					}

					_isAttacking = false;
					await EndCombatSession("SUCCESS.");
				}
				else
				{
					// ... (Logic AP kurang tetap sama) ...
					_hud.LogToTerminal($"> ERROR: Insufficient AP. Need {apCost}.", Colors.Red);
				}
			}
			// SKENARIO 2: Typo / Command Tidak Ada (GAGAL TOTAL)
			else
			{
				// 1. Kurangi AP sebagai denda
				_currentAP -= TypoPenaltyAP;

				// Pastikan AP tidak minus
				if (_currentAP < 0) _currentAP = 0;

				if (_hud != null) _hud.UpdateAP(_currentAP, MaxAP);

				// 2. Beri pesan Error yang menyeramkan
				_hud.LogToTerminal($"> SYNTAX ERROR: Command '{command}' unknown.", Colors.Red);
				_hud.LogToTerminal($"> PENALTY APPLIED: -{TypoPenaltyAP} AP.", Colors.Orange);

				// 3. Paksa Akhiri Giliran
				await EndCombatSession("CRITICAL FAILURE: SYSTEM HALTED.");
			}
		}

		// Helper Function untuk menutup sesi dan ganti giliran (Biar rapi)
		private async System.Threading.Tasks.Task EndCombatSession(string endMessage)
		{
			_hud.LogToTerminal($"> {endMessage}", Colors.Gray);
			_hud.LogToTerminal("> TERMINATING SESSION...", Colors.Gray);

			await ToSignal(GetTree().CreateTimer(1.5f), "timeout");

			_hud.ShowCombatPanel(false); // Panel tetap kita tutup agar bisa lihat animasi musuh

			if (_turnManager != null)
			{
				_turnManager.ForceEndPlayerTurn();
			}
		}

		public void TakeDamage(int damage)
		{
			_currentHP -= damage;
			ShowDamagePopup(damage);
			GD.Print($"WARNING: Player took {damage} damage! HP: {_currentHP}/{MaxHP}");

			// Efek berkedip merah (Visual Feedback)
			Modulate = Colors.Red;
			CreateTween().TweenProperty(this, "modulate", Colors.White, 0.2f);

			if (_currentHP <= 0)
			{
				Die();
			}
		}

		private void Die()
		{
			GD.Print("GAME OVER: SYSTEM FAILURE.");

			SetPhysicsProcess(false); // Matikan player
		}

		private void ShowDamagePopup(int amount)
		{
			if (DamagePopupScene != null)
			{
				// 1. Buat instance
				var popup = DamagePopupScene.Instantiate<MementoTest.UI.DamagePopup>();

				// 2. Masukkan ke scene tree (tambahkan sebagai child dari Level/Root, atau diri sendiri)
				// Karena kita sudah set 'TopLevel = true' di script popup, jadi child diri sendiri aman.
				AddChild(popup);

				// 3. Tentukan warna (Misal: Player kena hit = Merah, Musuh kena hit = Putih/Kuning)
				// Logika sederhana: Kalau ini script Player, warnanya Merah.
				Color color = (this is PlayerController) ? Colors.Red : Colors.Yellow;

				// 4. Jalankan animasi (Posisi muncul di atas kepala sedikit)
				popup.SetupAndAnimate(amount, GlobalPosition + new Vector2(0, -30), color);
			}
		}
	}
}