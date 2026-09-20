// C# port of the pure snake rules from Matthew's Python Snake project
// (snake_game.py, class SnakeGame) — same semantics, including the
// tail-vacating exception on self-collision.
namespace EvolutionaryStudio.Model.Snake
{
    public sealed class SnakeGame
    {
        public static readonly (int Dy, int Dx) Up = (-1, 0);
        public static readonly (int Dy, int Dx) Down = (1, 0);
        public static readonly (int Dy, int Dx) Left = (0, -1);
        public static readonly (int Dy, int Dx) Right = (0, 1);

        private readonly Random rng;

        public int Height { get; }
        public int Width { get; }
        public LinkedList<(int Y, int X)> Body { get; } = new();
        public HashSet<(int Y, int X)> BodySet { get; } = new();
        public (int Dy, int Dx) Direction { get; private set; }
        public (int Dy, int Dx) PendingDirection { get; private set; }
        public (int Y, int X) Fruit { get; private set; }
        public bool HasFruit { get; private set; }
        public int Score { get; private set; }
        public bool Alive { get; private set; } = true;
        public bool Won { get; private set; }

        public (int Y, int X) Head => Body.First.Value;
        public (int Y, int X) Tail => Body.Last.Value;

        public SnakeGame(int height, int width, int seed)
        {
            if (height < 5 || width < 5)
                throw new ArgumentException("board must be at least 5x5");
            Height = height;
            Width = width;
            rng = new Random(seed);

            int cy = height / 2, cx = width / 2;
            foreach (var cell in new[] { (cy, cx), (cy, cx - 1), (cy, cx - 2) })
            {
                Body.AddLast(cell);
                BodySet.Add(cell);
            }
            Direction = Right;
            PendingDirection = Right;
            PlaceFruit();
        }

        private void PlaceFruit()
        {
            var free = new List<(int, int)>();
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    if (!BodySet.Contains((y, x)))
                        free.Add((y, x));
            if (free.Count == 0)
            {
                HasFruit = false;
                Won = true;
                return;
            }
            Fruit = free[rng.Next(free.Count)];
            HasFruit = true;
        }

        /// <summary>Queue a new direction. 180-degree turns are silently rejected.</summary>
        public void SetDirection((int Dy, int Dx) direction)
        {
            if (direction.Dy == -Direction.Dy && direction.Dx == -Direction.Dx)
                return;
            PendingDirection = direction;
        }

        /// <summary>Advance one tick. Returns true if the snake is still alive.</summary>
        public bool Step()
        {
            if (!Alive) return false;

            Direction = PendingDirection;
            var (hy, hx) = Head;
            var newHead = (Y: hy + Direction.Dy, X: hx + Direction.Dx);

            if (newHead.Y < 0 || newHead.Y >= Height || newHead.X < 0 || newHead.X >= Width)
            {
                Alive = false;
                return false;
            }

            bool eating = HasFruit && newHead == Fruit;

            // the tail vacates this tick unless we're eating, so moving into
            // the current tail cell is legal when not eating
            (int, int)? removedTail = null;
            if (!eating)
            {
                removedTail = Tail;
                Body.RemoveLast();
                BodySet.Remove(removedTail.Value);
            }
            if (BodySet.Contains(newHead))
            {
                if (removedTail is (int, int) tail)   // leave the corpse intact
                {
                    Body.AddLast(tail);
                    BodySet.Add(tail);
                }
                Alive = false;
                return false;
            }

            Body.AddFirst(newHead);
            BodySet.Add(newHead);
            if (eating)
            {
                Score++;
                PlaceFruit();
            }
            return true;
        }
    }
}
