using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CubeSnake3D
{
    public class Game1 : Game
    {
        private GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;

        // 3D rendering
        private BasicEffect _effect;

        // grid & cube
        private const int GridSize = 4;          // grid per face (4x4 per face = 4x4x4 cube)
        private const float CubeSize = 4f;       // size of cube (world units)
        private const float Half = CubeSize / 2f;

        // snake state (face, x, y)
        private readonly LinkedList<Cell> _snake = new();
        private Cell _food;
        private Direction _direction = Direction.Right;
        private Direction _pendingDirection = Direction.Right;
        private bool _paused;
        private bool _gameOver;
        private int _updateMs = 200;
        private double _accumulator;

        // timing
        private double _elapsedMs;

        // camera
        private Matrix _view;
        private Matrix _projection;

        // drawing helpers
        private VertexPositionColor[] _cubeWire; // optional wireframe
        // Všechny strany sytě zelené
        private readonly Color[] _faceColors = {
            Color.LimeGreen, Color.LimeGreen, Color.LimeGreen,
            Color.LimeGreen, Color.LimeGreen, Color.LimeGreen
        };

        private Random _rnd = new();

        // Keyboard state tracking for single-press detection
        private KeyboardState _previousKeyboardState;

        public Game1()
        {
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;
        }

        protected override void Initialize()
        {
            // window
            _graphics.PreferredBackBufferWidth = 1000;
            _graphics.PreferredBackBufferHeight = 700;
            _graphics.ApplyChanges();

            // camera: slightly behind and above looking at origin
            _view = Matrix.CreateLookAt(new Vector3(6f, 6f, 6f), Vector3.Zero, Vector3.Up);
            _projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4,
                GraphicsDevice.Viewport.AspectRatio, 0.1f, 100f);

            // BasicEffect
            _effect = new BasicEffect(GraphicsDevice)
            {
                VertexColorEnabled = true,
                LightingEnabled = false,
                World = Matrix.Identity,
                View = _view,
                Projection = _projection
            };

            // start
            StartNewGame();

            base.Initialize();
        }

        private void StartNewGame()
        {
            _snake.Clear();
            // start on face 0 in the middle
            var start = new Cell(0, GridSize / 4, GridSize / 2);
            _snake.AddLast(start);
            _snake.AddLast(new Cell(start.Face, start.X - 1, start.Y));
            _snake.AddLast(new Cell(start.Face, start.X - 2, start.Y));
            _direction = Direction.Right;
            _pendingDirection = Direction.Right;
            _paused = false;
            _gameOver = false;
            _updateMs = 200;
            _accumulator = 0;
            _rnd = new Random();
            PlaceFood();
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);
            BuildCubeWire();
        }

        private void BuildCubeWire()
        {
            // optional: build wireframe vertices (not strictly necessary)
            _cubeWire = new VertexPositionColor[24];
            int i = 0;
            Vector3[] verts = {
                new Vector3(-Half, -Half, -Half),
                new Vector3(Half, -Half, -Half),
                new Vector3(Half, Half, -Half),
                new Vector3(-Half, Half, -Half),

                new Vector3(-Half, -Half, Half),
                new Vector3(Half, -Half, Half),
                new Vector3(Half, Half, Half),
                new Vector3(-Half, Half, Half)
            };
            // 12 edges (pairs)
            Action<int, int> addEdge = (a, b) =>
            {
                _cubeWire[i++] = new VertexPositionColor(verts[a], Color.Black);
                _cubeWire[i++] = new VertexPositionColor(verts[b], Color.Black);
            };
            addEdge(0, 1); addEdge(1, 2); addEdge(2, 3); addEdge(3, 0); // back
            addEdge(4, 5); addEdge(5, 6); addEdge(6, 7); addEdge(7, 4); // front
            addEdge(0, 4); addEdge(1, 5); addEdge(2, 6); addEdge(3, 7); // connections
        }

        protected override void Update(GameTime gameTime)
        {
            var kb = Keyboard.GetState();
            if (kb.IsKeyDown(Keys.Escape)) Exit();

            // directional input - pending to apply on tick
            if (kb.IsKeyDown(Keys.Up) || kb.IsKeyDown(Keys.W)) TrySetPending(Direction.Up);
            if (kb.IsKeyDown(Keys.Down) || kb.IsKeyDown(Keys.S)) TrySetPending(Direction.Down);
            if (kb.IsKeyDown(Keys.Left) || kb.IsKeyDown(Keys.A)) TrySetPending(Direction.Left);
            if (kb.IsKeyDown(Keys.Right) || kb.IsKeyDown(Keys.D)) TrySetPending(Direction.Right);

            // Fixed: Single-press detection for pause and restart
            if (kb.IsKeyDown(Keys.P) && !_previousKeyboardState.IsKeyDown(Keys.P))
                _paused = !_paused;
            if (kb.IsKeyDown(Keys.R) && !_previousKeyboardState.IsKeyDown(Keys.R) && _gameOver)
                StartNewGame();

            _previousKeyboardState = kb;

            if (_paused || _gameOver)
            {
                base.Update(gameTime);
                return;
            }

            _accumulator += gameTime.ElapsedGameTime.TotalMilliseconds;
            if (_accumulator >= _updateMs)
            {
                _accumulator -= _updateMs;
                // apply pending direction (no 180 flips)
                if (!IsOpposite(_direction, _pendingDirection))
                    _direction = _pendingDirection;
                Tick();
            }

            base.Update(gameTime);
        }

        private void TrySetPending(Direction d)
        {
            if (!IsOpposite(_direction, d))
                _pendingDirection = d;
        }

        private static bool IsOpposite(Direction a, Direction b)
        {
            return (a == Direction.Left && b == Direction.Right) || (a == Direction.Right && b == Direction.Left)
                || (a == Direction.Up && b == Direction.Down) || (a == Direction.Down && b == Direction.Up);
        }

        private void Tick()
        {
            var head = _snake.First.Value;
            var next = head.Move(_direction, GridSize);

            // simplified transitions: if out of bounds, map to another face with same coords clamped
            if (next.X < 0 || next.X >= GridSize || next.Y < 0 || next.Y >= GridSize)
            {
                // moveToNeighbor returns transformed cell (preserves direction); simple mapping used here
                next = MoveToNeighborFace(head, _direction);
            }

            // wall/self collision on new face/cell
            if (_snake.Any(s => s.Equals(next)))
            {
                _gameOver = true;
                return;
            }

            _snake.AddFirst(next);

            if (next.Equals(_food))
            {
                // eat
                PlaceFood();
                // speed up a bit
                _updateMs = Math.Max(60, _updateMs - 10);
            }
            else
            {
                _snake.RemoveLast();
            }
        }

        // simplified neighbor mapping (prototype): pick neighbor face deterministically
        private Cell MoveToNeighborFace(Cell from, Direction dir)
        {
            // Very simple scheme: map direction to a different face index.
            // This is intentionally simple for v1.0 prototype.
            int newFace = from.Face;
            int nx = from.X;
            int ny = from.Y;

            switch (dir)
            {
                case Direction.Left:
                    newFace = (from.Face + 3) % 6;
                    nx = GridSize - 1;
                    break;
                case Direction.Right:
                    newFace = (from.Face + 1) % 6;
                    nx = 0;
                    break;
                case Direction.Up:
                    newFace = (from.Face + 2) % 6;
                    ny = 0;
                    break;
                case Direction.Down:
                    newFace = (from.Face + 4) % 6;
                    ny = GridSize - 1;
                    break;
            }

            // clamp just in case
            nx = Math.Clamp(nx, 0, GridSize - 1);
            ny = Math.Clamp(ny, 0, GridSize - 1);
            return new Cell(newFace, nx, ny);
        }

        private void PlaceFood()
        {
            Cell c;
            do
            {
                c = new Cell(_rnd.Next(0, 6), _rnd.Next(0, GridSize), _rnd.Next(0, GridSize));
            } while (_snake.Any(s => s.Equals(c)));
            _food = c;
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.CornflowerBlue);

            // 3D: draw colored faces and snake cells in world space
            _effect.World = Matrix.Identity;
            _effect.View = _view;
            _effect.Projection = _projection;
            _effect.CurrentTechnique.Passes[0].Apply();

            // draw each face as colored quad - plná sytost barev
            for (int f = 0; f < 6; f++)
            {
                DrawFaceQuad(f, _faceColors[f]);  // plná sytá zelená
                DrawFaceGrid(f, Color.Black);      // výrazné černé čáry
            }

            // draw snake segments
            foreach (var seg in _snake)
            {
                DrawCellOnFace(seg, Color.DarkGreen);
            }

            // draw food
            DrawCellOnFace(_food, Color.Red);

            // draw wireframe cube edges (optional)
            GraphicsDevice.RasterizerState = new RasterizerState { CullMode = CullMode.None };
            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GraphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, _cubeWire, 0, _cubeWire.Length / 2);
            }

            base.Draw(gameTime);
        }

        // Draw a face colored quad (two triangles)
        private void DrawFaceQuad(int faceIndex, Color color)
        {
            var verts = FaceQuadVertices(faceIndex, color);
            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, verts, 0, 2);
            }
        }

        // Draw grid lines on face (simple thin quads)
        private void DrawFaceGrid(int faceIndex, Color color)
        {
            // draw vertical and horizontal lines as thin quads
            for (int i = 1; i < GridSize; i++)
            {
                // vertical line at column i
                var vline = FaceLineVertices(faceIndex, true, i, color);
                foreach (var pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, vline, 0, 2);
                }
                // horizontal line at row i
                var hline = FaceLineVertices(faceIndex, false, i, color);
                foreach (var pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, hline, 0, 2);
                }
            }
        }

        // Draw a square cell (centered) on a face
        private void DrawCellOnFace(Cell cell, Color color)
        {
            var verts = FaceCellVertices(cell, color);
            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, verts, 0, 2);
            }
        }

        // Build two triangles for face quad
        private VertexPositionColor[] FaceQuadVertices(int faceIndex, Color c)
        {
            Vector3 n = FaceNormal(faceIndex);
            Vector3 right = FaceRight(faceIndex);
            Vector3 up = FaceUp(faceIndex);

            float cellWorld = CubeSize / GridSize;
            float faceHalf = CubeSize / 2f;
            // corners in local face coords: (-half,-half) to (+half,+half)
            Vector3 bl = n * Half + (-right - up) * faceHalf; // bottom-left
            Vector3 br = n * Half + (right - up) * faceHalf;
            Vector3 tl = n * Half + (-right + up) * faceHalf;
            Vector3 tr = n * Half + (right + up) * faceHalf;

            return new[]
            {
                new VertexPositionColor(tl, c),
                new VertexPositionColor(bl, c),
                new VertexPositionColor(br, c),

                new VertexPositionColor(tl, c),
                new VertexPositionColor(br, c),
                new VertexPositionColor(tr, c)
            };
        }

        // thin quad for grid lines (vertical if isVertical true)
        private VertexPositionColor[] FaceLineVertices(int faceIndex, bool isVertical, int index, Color c)
        {
            Vector3 n = FaceNormal(faceIndex);
            Vector3 right = FaceRight(faceIndex);
            Vector3 up = FaceUp(faceIndex);

            float cellWorld = CubeSize / GridSize;
            float halfLine = 0.01f; // line thickness

            float coord = -Half + index * cellWorld; // coordinate along axis

            if (isVertical)
            {
                Vector3 center = n * Half + right * coord;
                // create thin rectangle along up axis centered at center
                Vector3 a = center + up * Half + (-right) * halfLine;
                Vector3 b = center - up * Half + (-right) * halfLine;
                Vector3 c1 = center - up * Half + (right) * halfLine;
                Vector3 d = center + up * Half + (right) * halfLine;
                return QuadVerts(a, b, c1, d, c);
            }
            else
            {
                Vector3 center = n * Half + up * coord;
                Vector3 a = center + right * Half + (-up) * halfLine;
                Vector3 b = center - right * Half + (-up) * halfLine;
                Vector3 c1 = center - right * Half + (up) * halfLine;
                Vector3 d = center + right * Half + (up) * halfLine;
                return QuadVerts(a, b, c1, d, c);
            }
        }

        // returns 2 triangles quad verts for a cell
        private VertexPositionColor[] FaceCellVertices(Cell cell, Color color)
        {
            Vector3 n = FaceNormal(cell.Face);
            Vector3 right = FaceRight(cell.Face);
            Vector3 up = FaceUp(cell.Face);

            float cellWorld = CubeSize / GridSize;
            // cell center: x -> right axis, y -> up axis; origin at center of face (-Half..Half)
            float cx = -Half + (cell.X + 0.5f) * cellWorld;
            float cy = -Half + (cell.Y + 0.5f) * cellWorld;
            Vector3 center = n * Half + right * cx + up * cy;

            float h = cellWorld * 0.45f; // slightly smaller than cell
            Vector3 a = center + (-right) * h + up * h;
            Vector3 b = center + (-right) * h + (-up) * h;
            Vector3 c = center + right * h + (-up) * h;
            Vector3 d = center + right * h + up * h;
            return QuadVerts(a, b, c, d, color);
        }

        private VertexPositionColor[] QuadVerts(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
        {
            return new[]
            {
                new VertexPositionColor(a, color),
                new VertexPositionColor(b, color),
                new VertexPositionColor(c, color),

                new VertexPositionColor(a, color),
                new VertexPositionColor(c, color),
                new VertexPositionColor(d, color)
            };
        }

        // face helpers: normal, right, up in world coords
        private Vector3 FaceNormal(int f) => f switch
        {
            0 => new Vector3(0, 0, 1),   // front (+Z)
            1 => new Vector3(0, 0, -1),  // back (-Z)
            2 => new Vector3(1, 0, 0),   // right (+X)
            3 => new Vector3(-1, 0, 0),  // left (-X)
            4 => new Vector3(0, 1, 0),   // top (+Y)
            5 => new Vector3(0, -1, 0),  // bottom (-Y)
            _ => Vector3.Zero
        };

        private Vector3 FaceRight(int f) => f switch
        {
            0 => new Vector3(1, 0, 0),
            1 => new Vector3(-1, 0, 0),
            2 => new Vector3(0, 0, -1),
            3 => new Vector3(0, 0, 1),
            4 => new Vector3(1, 0, 0),
            5 => new Vector3(1, 0, 0),
            _ => Vector3.UnitX
        };

        private Vector3 FaceUp(int f) => f switch
        {
            0 => new Vector3(0, 1, 0),
            1 => new Vector3(0, 1, 0),
            2 => new Vector3(0, 1, 0),
            3 => new Vector3(0, 1, 0),
            4 => new Vector3(0, 0, -1),
            5 => new Vector3(0, 0, 1),
            _ => Vector3.UnitY
        };

        // Cell struct with Move method - FIXED
        private readonly record struct Cell(int Face, int X, int Y)
        {
            public Cell Move(Direction dir, int gridSize)
            {
                return dir switch
                {
                    Direction.Up => new Cell(Face, X, Y + 1),
                    Direction.Down => new Cell(Face, X, Y - 1),
                    Direction.Left => new Cell(Face, X - 1, Y),
                    Direction.Right => new Cell(Face, X + 1, Y),
                    _ => this
                };
            }
        }

        private enum Direction { Up, Down, Left, Right }
    }
}