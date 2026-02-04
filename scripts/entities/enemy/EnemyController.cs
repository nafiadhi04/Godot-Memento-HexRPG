using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks; // Wajib untuk async/await
using MementoTest.Entities;   // Agar kenal PlayerController
using MementoTest.Resources;  // Agar kenal EnemySkill// [WAJIB] Tambahkan baris ini agar 'Task' dikenali!

using MementoTest.Core;

namespace MementoTest.Entities
{
	public partial class EnemyController : CharacterBody2D
	{
		[Export] public float MoveDuration = 0.3f;
		[Export] public int MaxHP = 50;
		[Export] public Godot.Collections.Array<EnemySkill> SkillList;
		[Export] public PackedScene DamagePopupScene;

		[ExportGroup("Combat Settings")] // Opsional: Biar rapi di Inspector
		[Export] public float ReactionTimeMelee = 1.5f; // Waktu untuk Parry (Jarak Dekat)
		[Export] public float ReactionTimeRanged = 2.0f;
		private int _currentHP;
		private PlayerController _targetPlayer;

		// Hapus variabel Timer lama jika masih ada
		// private Timer _moveTimer; 

		private MapManager _mapManager;
		private Vector2I _currentGridPos;
		private bool _isMoving = false;
		private Random _rng = new Random();
		private ProgressBar _healthBar;

		private MementoTest.UI.BattleHUD _hud;

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

			// 1. Setup HealthBar
			_healthBar = GetNodeOrNull<ProgressBar>("HealthBar");
			if (_healthBar != null)
			{
				_healthBar.MaxValue = MaxHP;
				_healthBar.Value = _currentHP;
				_healthBar.Visible = true;
			}

			// 2. Setup MapManager (Cek parent untuk jaga-jaga, tapi pakai Singleton lebih baik nanti)
			if (GetParent().HasNode("MapManager"))
			{
				_mapManager = GetParent().GetNode<MapManager>("MapManager");
				_currentGridPos = _mapManager.GetGridCoordinates(GlobalPosition);
				GlobalPosition = _mapManager.GetSnappedWorldPosition(GlobalPosition);
			}

			AddToGroup("Enemy");

			// 3. [FIX] Cari Player pakai Group (Anti Error Folder)
			var playerNode = GetTree().GetFirstNodeInGroup("Player");
			if (playerNode is PlayerController player)
			{
				_targetPlayer = player;
			}

			// 4. [FIX UTAMA] Cari HUD pakai Group (Wajib!)
			// Kita cari node apa saja yang punya grup "HUD"
			var hudNode = GetTree().GetFirstNodeInGroup("HUD");

			if (hudNode is MementoTest.UI.BattleHUD hud)
			{
				_hud = hud;
				GD.Print($"[ENEMY] {Name} SUKSES Connect ke HUD.");
			}
			else
			{
				// Kalau ini muncul, berarti kamu lupa langkah di Editor (Langkah 2 di bawah)
				GD.PrintErr($"[ENEMY] {Name} GAGAL Connect ke HUD! Node 'BattleHUD' belum masuk Group 'HUD'.");
			}


			FindTargetPlayer();
			SnapToNearestGrid();
		}

		private void SnapToNearestGrid()
		{
			// Cek dulu biar gak error kalau MapManager belum siap
			if (MapManager.Instance == null)
			{
				GD.PrintErr("[ENEMY] MapManager Instance belum siap!");
				return;
			}

			Vector2I gridCoords = MapManager.Instance.WorldToGrid(GlobalPosition);
			Vector2 centeredPos = MapManager.Instance.GridToWorld(gridCoords);
			GlobalPosition = centeredPos;
		}
		private void FindTargetPlayer()
		{
			// Cari node pertama yang ada di grup "Player"
			var playerNode = GetTree().GetFirstNodeInGroup("Player");

			if (playerNode is PlayerController player) // Ganti 'PlayerController' sesuai nama class script Playermu
			{
				_targetPlayer = player;
				GD.Print($"[ENEMY] {Name} menemukan target: {_targetPlayer.Name}");
			}
			else if (playerNode != null)
			{
				// Fallback jika scriptnya bukan PlayerController tapi Node2D biasa
				// Sesuaikan casting ini dengan script player kamu (misal: 'Player' atau 'CharacterBody2D')
				GD.PrintErr($"[ENEMY] Node ditemukan tapi tipe script salah!");
			}
		}
		// Fungsi ini dipanggil oleh TurnManager.cs
		// Kata kunci 'async Task' membuat fungsi ini bisa ditunggu (await)
		public async Task DoTurnAction()
		{
			List<Vector2I> validMoves = GetValidNeighbors();

			if (validMoves.Count > 0)
			{
				int randomIndex = GD.RandRange(0, validMoves.Count - 1);
				Vector2I targetGrid = validMoves[randomIndex];

				// Tunggu sampai animasi jalan selesai baru lanjut
				await MoveToGrid(targetGrid);
			}
			else
			{
				// Jika macet, diam sebentar (0.5 detik) seolah-olah mikir
				await ToSignal(GetTree().CreateTimer(0.5f), "timeout");
			}
		}

		private List<Vector2I> GetValidNeighbors()
		{
			List<Vector2I> neighbors = new List<Vector2I>();

			foreach (var direction in _hexNeighbors)
			{
				Vector2I neighborCell = _mapManager.GetNeighborCell(_currentGridPos, direction);

				// Cek 1: Apakah tanahnya bisa diinjak?
				if (_mapManager.IsTileWalkable(neighborCell))
				{
					// Cek 2: Apakah ada unit lain di sana? (Fitur baru)
					if (!_mapManager.IsTileOccupied(neighborCell))
					{
						neighbors.Add(neighborCell);
					}
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
				.SetTrans(Tween.TransitionType.Sine)
				.SetEase(Tween.EaseType.Out);

			// Tunggu sinyal 'finished' dari tween
			await ToSignal(tween, "finished");

			_currentGridPos = targetGrid;
			_isMoving = false;
		}

		public async Task ExecuteTurn()
		{
			if (_targetPlayer == null || !GodotObject.IsInstanceValid(_targetPlayer))
			{
				GD.Print($"{Name}: Tidak ada target.");
				return;
			}

			// 1. Hitung Jarak Real ke Player (Pixel)
			float distToPlayer = GlobalPosition.DistanceTo(_targetPlayer.GlobalPosition);
			GD.Print($"{Name} distance to player: {distToPlayer:F1}px");

			// 2. AI BERPIKIR: Cari skill apa yang bisa dipakai di jarak segini?
			// Kita filter SkillList: Ambil skill yang Range-nya >= Jarak Musuh ke Player
			var validSkills = SkillList.Where(s => s.AttackRange >= distToPlayer).ToList();

			if (validSkills.Count > 0)
			{
				// Kalau ada skill yang valid, pilih satu secara acak (Biar variatif)
				int index = _rng.Next(validSkills.Count);
				EnemySkill chosenSkill = validSkills[index];

				// Lakukan serangan
				await PerformSkill(chosenSkill);
			}
			else
			{
				// Kalau tidak ada skill yang nyampai (kejauhan)
				GD.Print($"{Name}: Target terlalu jauh untuk semua skill. Menunggu...");
				// (Nanti di sini kita masukkan logika Move/Jalan mendekat)
				await ToSignal(GetTree().CreateTimer(0.5f), "timeout");
			}

			
		}

		private async Task PerformSkill(EnemySkill skill)
		{
			GD.Print($"{Name} using skill [{skill.SkillName}]");

			Vector2 originalPos = GlobalPosition;
			Vector2 direction = originalPos.DirectionTo(_targetPlayer.GlobalPosition);
			Vector2 attackPos = originalPos + (direction * 30f); // Maju dikit

			// 1. Animasi Maju
			Tween attackTween = CreateTween();
			attackTween.TweenProperty(this, "global_position", attackPos, 0.2f);
			await ToSignal(attackTween, "finished");

			// 2. Minta Reaksi Player (Panggil HUD yang baru)
			bool isDodged = false;

			if (_hud != null)
			{
				string word = (skill.AttackRange < 150) ? "parry" : "dodge";
				float time = (skill.AttackRange < 150) ? ReactionTimeMelee : ReactionTimeRanged;

				// Await ini SEKARANG AMAN karena pakai TaskCompletionSource
				isDodged = await _hud.WaitForPlayerReaction(word, time);
			}

			// 3. Eksekusi Damage
			if (isDodged)
			{
				GD.Print(">>> REACTION SUCCESS! 0 DAMAGE.");
				_targetPlayer.TakeDamage(0); // Damage 0 -> Muncul Popup "0" atau "MISS"
			}
			else
			{
				GD.Print(">>> FAILED! TAKING DAMAGE.");
				_targetPlayer.TakeDamage(skill.Damage);
			}

			// 4. Mundur
			Tween returnTween = CreateTween();
			returnTween.TweenProperty(this, "global_position", originalPos, 0.2f);
			await ToSignal(returnTween, "finished");
		}
		public void TakeDamage(int damage)
		{
			_currentHP -= damage;

			if (_healthBar != null)
			{
				_healthBar.Value = _currentHP;
			}

			ShowDamagePopup(damage);
			Modulate = Colors.Red;
			CreateTween().TweenProperty(this, "modulate", Colors.White, 0.2f);

			if (_currentHP <= 0)
			{
				Die();
			}
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
				Color color = Colors.Yellow;

				// 4. Jalankan animasi (Posisi muncul di atas kepala sedikit)
				popup.SetupAndAnimate(amount, GlobalPosition + new Vector2(0, -30), color);
			}
		}

		private async void Die()
		{
			GD.Print($"ENEMY DEFEATED: {Name}");

			// 1. Matikan Interaksi (PENTING)
			// Supaya player tidak bisa klik musuh ini lagi saat animasi mati berjalan
			// Kita matikan CollisionShape-nya
			var collision = GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
			if (collision != null)
			{
				collision.SetDeferred("disabled", true);
			}

			// Sembunyikan Health Bar biar rapi
			if (_healthBar != null) _healthBar.Visible = false;

			// 2. Animasi Kematian (Juicy!)
			Tween tween = CreateTween();
			tween.SetParallel(true); // Jalankan animasi secara bersamaan

			// Fade Out (Transparan)
			tween.TweenProperty(this, "modulate:a", 0f, 0.5f)
				.SetTrans(Tween.TransitionType.Expo)
				.SetEase(Tween.EaseType.Out);

			// Shrink (Mengecil sampai hilang)
			tween.TweenProperty(this, "scale", Vector2.Zero, 0.5f)
				.SetTrans(Tween.TransitionType.Back)
				.SetEase(Tween.EaseType.In);

			// Putar sedikit (opsional, biar dramatis)
			tween.TweenProperty(this, "rotation_degrees", 360f, 0.5f);

			// Tunggu animasi selesai
			await ToSignal(tween, "finished");

			// 3. Hapus Object dari Memory
			QueueFree();
		}
	}
}