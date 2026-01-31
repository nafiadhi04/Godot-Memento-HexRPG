using Godot;
using System;

namespace MementoTest.Core
{
	public partial class MapManager : TileMapLayer
	{
		public override void _Ready()
		{
			GD.Print("MapManager: Grid system initialized successfully.");
		}

		/// <summary>
		/// Mengambil posisi pusat (Center) dari sebuah hexagon berdasarkan koordinat dunia.
		/// Berguna untuk "snapping" posisi karakter agar pas di tengah hex.
		/// </summary>
		public Vector2 GetSnappedWorldPosition(Vector2 worldPos)
		{
			// LocalToMap akan menghitung berdasarkan mode Flat Top (Vertical Offset) 
			// yang sudah kita set di Inspector
			Vector2I mapCoords = LocalToMap(ToLocal(worldPos));

			// MapToLocal akan mengembalikan titik pusat (pivot) hexagon tersebut
			return MapToLocal(mapCoords);
		}

		/// <summary>
		/// Mengambil koordinat grid (Axial/Offset) dari posisi dunia.
		/// </summary>
		public Vector2I GetGridCoordinates(Vector2 worldPos)
		{
			return LocalToMap(ToLocal(worldPos));
		}

		/// <summary>
		/// Mengecek apakah sebuah tile bisa dilewati berdasarkan Custom Data Layer "is_walkable".
		/// </summary>
		public bool IsTileWalkable(Vector2I mapCoords)
		{
			TileData data = GetCellTileData(mapCoords);
			if (data == null) return false;

			// Membaca data "is_walkable" yang kita buat di Editor
			Variant walkable = data.GetCustomData("is_walkable");
			return walkable.AsBool();
		}

		public bool IsNeighbor(Vector2I currentCoords, Vector2I targetCoords)
		{
			// Jika mengeklik diri sendiri, anggap 'too far' atau abaikan
			if (currentCoords == targetCoords) return false;

			// Daftar 6 arah tetangga untuk hexagon
			TileSet.CellNeighbor[] neighbors = {
		TileSet.CellNeighbor.TopSide,
		TileSet.CellNeighbor.BottomSide,
		TileSet.CellNeighbor.TopLeftSide,
		TileSet.CellNeighbor.TopRightSide,
		TileSet.CellNeighbor.BottomLeftSide,
		TileSet.CellNeighbor.BottomRightSide
	};

			foreach (var side in neighbors)
			{
				// Fungsi bawaan Godot untuk mendapatkan koordinat tetangga
				if (GetNeighborCell(currentCoords, side) == targetCoords)
				{
					return true;
				}
			}
			return false;
		}
	}
}