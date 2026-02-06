using Godot;
using MementoTest.UI;
using System.Collections.Generic;
using System.Threading.Tasks; // Perlu untuk Task
using MementoTest.Entities;

namespace MementoTest.Core
{
	public partial class TurnManager : Node
	{
		[Signal] public delegate void PlayerTurnStartedEventHandler();
		[Signal] public delegate void EnemyTurnStartedEventHandler();

		public enum TurnState { Player, Enemy }
		public TurnState CurrentTurn { get; private set; }

		private BattleHUD _battleHUD;

		// Referensi ke Player untuk cek jarak (Opsional tapi disarankan)
		private PlayerController _player;

		public override void _Ready()
		{
			// Cari HUD
			if (GetParent().HasNode("BattleHUD"))
			{
				_battleHUD = GetParent().GetNode<BattleHUD>("BattleHUD");
				_battleHUD.EndTurnRequested += OnEndTurnPressed;
			}

			// Cari Player (Pastikan Player ada di Group "Player" atau cari manual)
			// Ini berguna agar musuh yang jauh tidak ikut menyerang
			var players = GetTree().GetNodesInGroup("Player");
			if (players.Count > 0)
				_player = players[0] as PlayerController;


			CallDeferred(MethodName.StartPlayerTurn);
		}

		private void StartPlayerTurn()
		{
			CurrentTurn = TurnState.Player;

			if (_battleHUD != null)
			{
				_battleHUD.SetEndTurnButtonInteractable(true);
				_battleHUD.UpdateTurnLabel("PLAYER PHASE");
			}

			_battleHUD.EnterPlayerCommandPhase(
	_player.GetAvailableSkillCommands()
);


			EmitSignal(SignalName.PlayerTurnStarted);
		}

		public void ForceEndPlayerTurn()
		{
			OnEndTurnPressed();
		}

		private void OnEndTurnPressed()
		{
			if (CurrentTurn != TurnState.Player) return;
			StartEnemyTurn();
		}

		// --- BAGIAN YANG DIPERBAIKI ---
		public async void StartEnemyTurn()
		{
			CurrentTurn = TurnState.Enemy;
			GD.Print("--- ENEMY PHASE START ---");
			if (_battleHUD != null) _battleHUD.UpdateTurnLabel("ENEMY TURN");

			// 1. [FIX] Cari musuh menggunakan GROUP, bukan Children.
			// Ini akan menemukan musuh meskipun mereka ada di dalam folder Area1, Area2, dll.
			var enemyNodes = GetTree().GetNodesInGroup("Enemy");

			bool anyEnemyActed = false;

			// 2. Eksekusi satu per satu
			foreach (var node in enemyNodes)
			{
				// Validasi: Pastikan node adalah EnemyController dan masih hidup
				if (node is EnemyController enemy && IsInstanceValid(enemy) && !enemy.IsQueuedForDeletion())
				{
					// [LOGIKA JARAK] 
					// Cek jarak ke player. Jika terlalu jauh (> 800 pixel), skip giliran ini.
					// Ini mencegah Boss di Area 5 menyerang saat kamu masih di Area 1.
					if (_player != null)
					{
						float dist = enemy.GlobalPosition.DistanceTo(_player.GlobalPosition);
						if (dist > 800) // Angka 800 bisa disesuaikan dengan ukuran layar/area
						{
							continue; // Skip musuh ini, lanjut ke musuh berikutnya
						}
					}

					anyEnemyActed = true;

					// Jalankan turn musuh
					// Pastikan di EnemyController fungsi ExecuteTurn mereturn Task
					await enemy.ExecuteTurn();

					// Jeda sedikit antar musuh
					await ToSignal(GetTree().CreateTimer(0.5f), "timeout");
				}
			}

			if (!anyEnemyActed)
			{
				GD.Print("Tidak ada musuh aktif di sekitar.");
				// Jeda sebentar biar tidak terlalu cepat pindah phase
				await ToSignal(GetTree().CreateTimer(0.5f), "timeout");
			}

			_battleHUD.ExitPlayerCommandPhase();

			// 3. Kembalikan giliran ke Player
			StartPlayerTurn();
		}
	}
}