using Godot;
using System;
using System.Threading.Tasks;
using MementoTest.Core;

namespace MementoTest.Entities
{
	public partial class PlayerController : CharacterBody2D
	{
		// Variabel yang bisa diatur di Inspector (No Hardcoded Values)
		[Export] public float MoveDuration = 0.25f;
		[Export] public int MaxHealth = 100;
		[Export] public int ActionPoints = 5;

		private Vector2I _currentGridPos;
		private bool _isMoving = false;
		private MapManager _mapManager;

		public override void _Ready()
		{
			// 1. Cari node MapManager
			_mapManager = GetParent().GetNode<MapManager>("MapManager");

			// 2. Ambil koordinat grid berdasarkan tempat kamu menaruh Player di Editor
			// Misal kamu taruh di (105.5, 200.1), ini akan dikonversi jadi Grid (2, 3)
			_currentGridPos = _mapManager.GetGridCoordinates(GlobalPosition);

			// 3. PENTING: Konversi balik Grid (2, 3) menjadi Posisi Dunia yang presisi (Center)
			// Lalu terapkan ke posisi Player
			GlobalPosition = _mapManager.GetSnappedWorldPosition(GlobalPosition);

			// Inisialisasi Action Points (jika ada UI update nanti)
			GD.Print($"Player Start Snapped to Grid: {_currentGridPos} at World Pos: {GlobalPosition}");
		}

		/// <summary>
		/// Fungsi dasar untuk memindahkan player ke posisi target.
		/// Nantinya akan dipicu oleh CommandSystem.
		/// </summary>
		public async Task MoveToPosition(Vector2 targetWorldPos, Vector2I gridCoords)
		{
			if (_isMoving) return;

			_isMoving = true;

			// Menggunakan Tween untuk pergerakan halus (Standar Godot 4)
			Tween tween = CreateTween();
			tween.SetTrans(Tween.TransitionType.Sine);
			tween.SetEase(Tween.EaseType.InOut);

			tween.TweenProperty(this, "global_position", targetWorldPos, MoveDuration);

			// Tunggu sampai animasi selesai
			await ToSignal(tween, "finished");

			_currentGridPos = gridCoords;
			_isMoving = false;

			GD.Print($"Player moved to: {_currentGridPos}");
		}
		public override void _Input(InputEvent @event)
		{
			if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Left)
			{
				if (_isMoving) return; // Jangan input saat sedang jalan

				Vector2 mousePos = GetGlobalMousePosition();
				Vector2I targetGrid = _mapManager.GetGridCoordinates(mousePos);

				// Validasi 1: Apakah tile bisa diinjak?
				// Validasi 2: Apakah tile tersebut adalah tetangga (jarak 1)?
				if (_mapManager.IsTileWalkable(targetGrid) && _mapManager.IsNeighbor(_currentGridPos, targetGrid))
				{
					Vector2 snapPos = _mapManager.GetSnappedWorldPosition(mousePos);

					// Kurangi Action Point (AP) sebagai bonus poin penilaian
					if (ActionPoints > 0)
					{
						ActionPoints--;
						GD.Print($"Moving to {targetGrid}. Remaining AP: {ActionPoints}");
						_ = MoveToPosition(snapPos, targetGrid);
					}
					else
					{
						GD.Print("Out of Action Points!");
					}
				}
				else
				{
					GD.Print("Target too far or not walkable!");
				}
			}
		}
	}

	
}