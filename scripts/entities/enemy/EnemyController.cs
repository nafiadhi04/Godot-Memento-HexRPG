using Godot;
using System;
using System.Collections.Generic; 
using MementoTest.Core;

namespace MementoTest.Entities
{
	public partial class EnemyController : CharacterBody2D
	{
		[Export] public float MoveInterval = 2.0f; // Bisa diatur di Inspector (2 detik)
		[Export] public float MoveDuration = 0.3f; // Kecepatan animasi gerak

		private MapManager _mapManager;
		private Vector2I _currentGridPos;
		private Timer _moveTimer;
		private bool _isMoving = false;

		// Daftar arah tetangga untuk Hexagon
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
			// 1. Setup MapManager
			if (GetParent().HasNode("MapManager"))
			{
				_mapManager = GetParent().GetNode<MapManager>("MapManager");

				// Snap posisi awal ke grid
				_currentGridPos = _mapManager.GetGridCoordinates(GlobalPosition);
				GlobalPosition = _mapManager.GetSnappedWorldPosition(GlobalPosition);
			}
			else
			{
				GD.PrintErr("Enemy: MapManager not found!");
				return;
			}

			// 2. Setup Timer untuk AI
			SetupAITimer();
		}

		private void SetupAITimer()
		{
			_moveTimer = new Timer();
			_moveTimer.Name = "AIMoveTimer";
			_moveTimer.WaitTime = MoveInterval;
			_moveTimer.OneShot = false; // Looping terus
			_moveTimer.Autostart = true;

			// Hubungkan sinyal timeout ke fungsi gerak
			_moveTimer.Timeout += OnTimerTimeout;

			AddChild(_moveTimer); // Masukkan timer ke scene
		}

		/// <summary>
		/// Fungsi otak AI: Dipanggil setiap 2 detik
		/// </summary>
		private void OnTimerTimeout()
		{
			if (_isMoving) return;

			// Langkah 1: Cari semua tetangga yang valid
			List<Vector2I> validMoves = GetValidNeighbors();

			// Langkah 2: Jika ada jalan, pilih satu secara acak
			if (validMoves.Count > 0)
			{
				// Mengambil index acak dari list
				int randomIndex = GD.RandRange(0, validMoves.Count - 1);
				Vector2I targetGrid = validMoves[randomIndex];

				// Langkah 3: Gerakkan musuh
				MoveToGrid(targetGrid);
			}
			else
			{
				GD.Print("Enemy: Stuck! No valid moves.");
			}
		}

		private List<Vector2I> GetValidNeighbors()
		{
			List<Vector2I> neighbors = new List<Vector2I>();

			foreach (var direction in _hexNeighbors)
			{
				// Minta koordinat tetangga ke MapManager (Fungsi bawaan Godot)
				Vector2I neighborCell = _mapManager.GetNeighborCell(_currentGridPos, direction);

				// Filter: Hanya masukkan jika tile tersebut Walkable (Bukan air/tembok)
				if (_mapManager.IsTileWalkable(neighborCell))
				{
					neighbors.Add(neighborCell);
				}
			}

			return neighbors;
		}

		private async void MoveToGrid(Vector2I targetGrid)
		{
			_isMoving = true;

			// Hitung posisi dunia (pixel) dari grid target
			Vector2 targetWorldPos = _mapManager.MapToLocal(targetGrid);

			// Animasi Gerak (Tween)
			Tween tween = CreateTween();
			tween.TweenProperty(this, "global_position", targetWorldPos, MoveDuration)
				.SetTrans(Tween.TransitionType.Sine)
				.SetEase(Tween.EaseType.Out);

			await ToSignal(tween, "finished");

			// Update data posisi setelah animasi selesai
			_currentGridPos = targetGrid;
			_isMoving = false;

			GD.Print($"Enemy moved to {_currentGridPos}");
		}
	}
}