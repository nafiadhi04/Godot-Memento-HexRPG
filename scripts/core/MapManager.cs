using Godot;
using System;

namespace MementoTest.Core
{
	public partial class MapManager : TileMapLayer
	{
		// Variabel untuk Kursor Visual
		private Line2D _highlightCursor;

		public override void _Ready()
		{
			GD.Print("MapManager: Grid system initialized.");

			// --- FITUR BARU: Auto-Create Cursor ---
			CreateHighlightCursor();
		}

		public override void _Process(double delta)
		{
			// --- FITUR BARU: Logic Kursor ---
			UpdateCursorPosition();
		}

		private void CreateHighlightCursor()
		{
			// Membuat node Line2D secara coding (tanpa perlu add node di Scene)
			_highlightCursor = new Line2D();
			_highlightCursor.Width = 2.0f;           // Ketebalan garis
			_highlightCursor.DefaultColor = new Color(1, 1, 0, 0.7f); // Warna Kuning Transparan
			_highlightCursor.Closed = true;          // Agar garis nyambung jadi lingkaran/hex
			_highlightCursor.ZIndex = 5;             // Pastikan di atas player/tanah

			// Membuat bentuk Hexagon (Flat Top)
			// Ukuran disesuaikan dengan Tile Size kamu (32x28)
			// Kamu bisa ubah angka ini jika bentuknya kurang pas
			float w = 14.0f; // Setengah lebar (agak dikurangi biar border ada di dalam)
			float h = 12.0f; // Setengah tinggi
			float m = 7.0f;  // Kemiringan sudut (untuk hex)

			Vector2[] hexPoints = new Vector2[]
			{
				new Vector2(-m, -h), // Kiri Atas
                new Vector2(m, -h),  // Kanan Atas
                new Vector2(w, 0),   // Kanan Tengah
                new Vector2(m, h),   // Kanan Bawah
                new Vector2(-m, h),  // Kiri Bawah
                new Vector2(-w, 0)   // Kiri Tengah
            };

			_highlightCursor.Points = hexPoints;
			_highlightCursor.Visible = false; // Sembunyikan dulu

			AddChild(_highlightCursor); // Masukkan ke dalam Scene
		}

		private void UpdateCursorPosition()
		{
			// Ambil posisi mouse
			Vector2 mousePos = GetGlobalMousePosition();
			Vector2I gridPos = GetGridCoordinates(mousePos);

			// Validasi: Apakah tile ini valid/bisa diinjak?
			// Kita gunakan IsTileWalkable yang sudah kita buat sebelumnya
			bool isValid = IsTileWalkable(gridPos);

			if (isValid)
			{
				_highlightCursor.Visible = true;

				// PENTING: Pindahkan kursor ke tengah grid (Snapping)
				// Kita gunakan fungsi GetSnappedWorldPosition yang sudah ada
				Vector2 visualOffset = new Vector2(0, 7); 
				_highlightCursor.Position = GetSnappedWorldPosition(mousePos) + visualOffset;
			}
			else
			{
				// Sembunyikan jika mouse keluar map atau di atas air
				_highlightCursor.Visible = false;
			}
		}

		// --- FUNGSI LAMA (JANGAN DIHAPUS) ---

		public Vector2 GetSnappedWorldPosition(Vector2 worldPos)
		{
			Vector2I mapCoords = LocalToMap(ToLocal(worldPos));
			return MapToLocal(mapCoords);
		}

		public Vector2I GetGridCoordinates(Vector2 worldPos)
		{
			return LocalToMap(ToLocal(worldPos));
		}

		public bool IsTileWalkable(Vector2I mapCoords)
		{
			if (GetCellSourceId(mapCoords) == -1) return false;

			TileData data = GetCellTileData(mapCoords);
			if (data == null) return false;

			Variant walkable = data.GetCustomData("is_walkable");
			if (walkable.VariantType == Variant.Type.Nil) return true;

			return walkable.AsBool();
		}

		public bool IsTileOccupied(Vector2I targetGridCoords)
		{
			var enemies = GetTree().GetNodesInGroup("Enemy");
			var players = GetTree().GetNodesInGroup("Player");

			foreach (Node2D entity in enemies)
			{
				// Cek jarak aman karena posisi float tidak selalu persis sama
				if (GetGridCoordinates(entity.GlobalPosition) == targetGridCoords) return true;
			}
			foreach (Node2D entity in players)
			{
				if (GetGridCoordinates(entity.GlobalPosition) == targetGridCoords) return true;
			}
			return false;
		}

		public bool IsNeighbor(Vector2I currentCoords, Vector2I targetCoords)
		{
			if (currentCoords == targetCoords) return false;
			TileSet.CellNeighbor[] neighbors = {
				TileSet.CellNeighbor.TopSide, TileSet.CellNeighbor.BottomSide,
				TileSet.CellNeighbor.TopLeftSide, TileSet.CellNeighbor.TopRightSide,
				TileSet.CellNeighbor.BottomLeftSide, TileSet.CellNeighbor.BottomRightSide
			};
			foreach (var side in neighbors)
			{
				if (GetNeighborCell(currentCoords, side) == targetCoords) return true;
			}
			return false;
		}
	}
}