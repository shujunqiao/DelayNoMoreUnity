using System;

namespace shared {
    public class CollisionSpace {
        CollisionCell[,] Cells;
        int CellWidth, CellHeight; // Width and Height of each Cell in "world-space" / pixels / whatever

        public CollisionSpace(int spaceWidth, int spaceHeight, int cellWidth, int cellHeight) {
            CellWidth = cellWidth;
            CellHeight = cellHeight;

            int cellCntW = spaceWidth / cellWidth;
            int cellCntH = spaceHeight / cellHeight;

            Cells = new CollisionCell[cellCntH, cellCntW];
            for (int y = 0; y < cellCntH; y++) {
                for (int x = 0; x < cellCntW; x++) {
                    Cells[y, x] = new CollisionCell(x, y);
                }
            }
        }

		// WorldToSpace converts from a world position (x, y) to a position in the Space (a grid-based position).
		public (int, int) WorldToSpace(float x, float y) {
			int fx = (int)(Math.Floor(x / CellWidth));
			int fy = (int)(Math.Floor(y / CellHeight));
			return (fx, fy);
		}

		// SpaceToWorld converts from a position in the Space (on a grid) to a world-based position, given the size of the Space when first created.
		public (float, float) SpaceToWorld(int x, int y) {
			float fx = (float)(x * CellWidth);
			float fy = (float)(y * CellHeight);
			return (fx, fy);
		}

		public CollisionCell? GetCell(int x, int y) {
			if (y >= 0 && y < Cells.GetLength(0) && x >= 0 && x < Cells.GetLength(1)) {
				return Cells[y, x];
			}
			return null;
		}
        public void AddSingle(Collider collider) {
			collider.Space = this;

            // We call Update() once to make sure the object gets its cells added.
            collider.Update();
		}

        public void RemoveSingle(Collider collider) {
			while (0 < collider.TouchingCells.Cnt) {
				var (_, cell) = collider.TouchingCells.Pop();
				if (null != cell) {
					cell.unregister(collider);
				}
			}

			collider.Space = null;
		}
    }
}
