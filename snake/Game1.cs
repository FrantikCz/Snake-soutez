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
        private const int GridSize = 4;
        private const float CubeSize = 4f;
        private static readonly float Half = CubeSize / 2f;

        // snake state - now simpler, always moving in screen space
        private readonly LinkedList<Cell> _snake = new();
        private Cell _food;
        private Direction _screenDirection = Direction.Right; // direction in screen space
        private Direction _pendingDirection = Direction.Right;
        private bool _paused;
        private bool _gameOver;
        private int _updateMs = 200;
        private double _accumulator;

        // camera - fixed position
        private Matrix _view;
        private Matrix _projection;

        // cube rotation - this rotates, not the camera
        private float targetRotX = 0f;
        private float targetRotY = 0f;
        private float currentRotX = 0f;
        private float currentRotY = 0f;
        private const float RotationSpeed = 0.1f;

        // drawing helpers
        private VertexPositionColor[] _cubeWire;
        private readonly Color[] _faceColors = {
            Color.LimeGreen, Color.LimeGreen, Color.LimeGreen,
            Color.LimeGreen, Color.LimeGreen, Color.LimeGreen
        };

        private Random _rnd = new();
        private KeyboardState _prevKb;

        public Game1()
        {
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;
        }

        protected override void Initialize()
        {
            _graphics.PreferredBackBufferWidth = 1000;
            _graphics.PreferredBackBufferHeight = 700;
            _graphics.ApplyChanges();

            // camera: looking straight at front face
            _view = Matrix.CreateLookAt(new Vector3(0f, 0f, 8f), Vector3.Zero, Vector3.Up);
            _projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4,
                GraphicsDevice.Viewport.AspectRatio, 0.1f, 100f);

            _effect = new BasicEffect(GraphicsDevice)
            {
                VertexColorEnabled = true,
                LightingEnabled = false,
                World = Matrix.Identity,
                View = _view,
                Projection = _projection
            };

            BuildCubeWire();
            StartNewGame();

            base.Initialize();
        }

        private void StartNewGame()
        {
            _snake.Clear();

            // Start on face 0 (front face)
            var start = new Cell(0, GridSize / 2, GridSize / 2);
            _snake.AddLast(start);
            _snake.AddLast(new Cell(start.Face, start.X - 1, start.Y));
            _snake.AddLast(new Cell(start.Face, start.X - 2, start.Y));

            _screenDirection = Direction.Right;
            _pendingDirection = Direction.Right;
            _paused = false;
            _gameOver = false;
            _updateMs = 200;
            _accumulator = 0;
            _rnd = new Random();

            // Reset rotation
            currentRotX = 0f;
            currentRotY = 0f;
            targetRotX = 0f;
            targetRotY = 0f;

            PlaceFood();
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);
        }

        private void BuildCubeWire()
        {
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
            Action<int, int> addEdge = (a, b) =>
            {
                _cubeWire[i++] = new VertexPositionColor(verts[a], Color.Black);
                _cubeWire[i++] = new VertexPositionColor(verts[b], Color.Black);
            };
            addEdge(0, 1); addEdge(1, 2); addEdge(2, 3); addEdge(3, 0);
            addEdge(4, 5); addEdge(5, 6); addEdge(6, 7); addEdge(7, 4);
            addEdge(0, 4); addEdge(1, 5); addEdge(2, 6); addEdge(3, 7);
        }

        protected override void Update(GameTime gameTime)
        {
            var kb = Keyboard.GetState();
            if (kb.IsKeyDown(Keys.Escape)) Exit();

            // Simple screen-space directional input
            if (kb.IsKeyDown(Keys.Up) || kb.IsKeyDown(Keys.W))
                TrySetPending(Direction.Up);
            if (kb.IsKeyDown(Keys.Down) || kb.IsKeyDown(Keys.S))
                TrySetPending(Direction.Down);
            if (kb.IsKeyDown(Keys.Left) || kb.IsKeyDown(Keys.A))
                TrySetPending(Direction.Left);
            if (kb.IsKeyDown(Keys.Right) || kb.IsKeyDown(Keys.D))
                TrySetPending(Direction.Right);

            if (kb.IsKeyDown(Keys.P) && !_prevKb.IsKeyDown(Keys.P))
                _paused = !_paused;
            if (kb.IsKeyDown(Keys.R) && !_prevKb.IsKeyDown(Keys.R) && _gameOver)
                StartNewGame();

            _prevKb = kb;

            if (_paused || _gameOver)
            {
                base.Update(gameTime);
                return;
            }

            // Smooth rotation
            currentRotX += (targetRotX - currentRotX) * RotationSpeed;
            currentRotY += (targetRotY - currentRotY) * RotationSpeed;

            _accumulator += gameTime.ElapsedGameTime.TotalMilliseconds;
            if (_accumulator >= _updateMs)
            {
                _accumulator -= _updateMs;

                if (!IsOpposite(_screenDirection, _pendingDirection))
                    _screenDirection = _pendingDirection;

                Tick();
            }

            base.Update(gameTime);
        }

        private void TrySetPending(Direction d)
        {
            if (!IsOpposite(_screenDirection, d))
                _pendingDirection = d;
        }

        private static bool IsOpposite(Direction a, Direction b)
        {
            return (a == Direction.Left && b == Direction.Right) ||
                   (a == Direction.Right && b == Direction.Left) ||
                   (a == Direction.Up && b == Direction.Down) ||
                   (a == Direction.Down && b == Direction.Up);
        }

        private void Tick()
        {
            var head = _snake.First.Value;

            // Move in screen-space direction
            var candidate = head.Move(_screenDirection);

            // Check if we need to wrap to another face
            if (candidate.X < 0 || candidate.X >= GridSize ||
                candidate.Y < 0 || candidate.Y >= GridSize)
            {
                candidate = WrapToNextFace(head, _screenDirection);
            }

            // Collision check
            if (_snake.Any(s => s.Equals(candidate)))
            {
                _gameOver = true;
                return;
            }

            _snake.AddFirst(candidate);

            if (candidate.Equals(_food))
            {
                PlaceFood();
                _updateMs = Math.Max(60, _updateMs - 10);
            }
            else
            {
                _snake.RemoveLast();
            }
        }

        private Cell WrapToNextFace(Cell from, Direction dir)
        {
            int newFace = from.Face;
            int nx = from.X;
            int ny = from.Y;

            // Simple face transitions - cube rotates to bring new face to front
            switch (from.Face)
            {
                case 0: // Front face
                    if (dir == Direction.Right)
                    {
                        newFace = 2; // Right face
                        nx = 0;
                        ny = from.Y;
                        targetRotY -= MathHelper.PiOver2;
                    }
                    else if (dir == Direction.Left)
                    {
                        newFace = 3; // Left face
                        nx = GridSize - 1;
                        ny = from.Y;
                        targetRotY += MathHelper.PiOver2;
                    }
                    else if (dir == Direction.Up)
                    {
                        newFace = 4; // Top face
                        nx = from.X;
                        ny = 0;
                        targetRotX += MathHelper.PiOver2;
                    }
                    else if (dir == Direction.Down)
                    {
                        newFace = 5; // Bottom face
                        nx = from.X;
                        ny = GridSize - 1;
                        targetRotX -= MathHelper.PiOver2;
                    }
                    break;

                case 2: // Right face (now front after rotation)
                    if (dir == Direction.Right)
                    {
                        newFace = 1; // Back face
                        nx = 0;
                        ny = from.Y;
                        targetRotY -= MathHelper.PiOver2;
                    }
                    else if (dir == Direction.Left)
                    {
                        newFace = 0; // Front face
                        nx = GridSize - 1;
                        ny = from.Y;
                        targetRotY += MathHelper.PiOver2;
                    }
                    else if (dir == Direction.Up)
                    {
                        newFace = 4; // Top face
                        nx = from.X;
                        ny = 0;
                        targetRotX += MathHelper.PiOver2;
                    }
                    else if (dir == Direction.Down)
                    {
                        newFace = 5; // Bottom face
                        nx = from.X;
                        ny = GridSize - 1;
                        targetRotX -= MathHelper.PiOver2;
                    }
                    break;

                case 3: // Left face
                    if (dir == Direction.Right)
                    {
                        newFace = 0; // Front face
                        nx = 0;
                        ny = from.Y;
                        targetRotY -= MathHelper.PiOver2;
                    }
                    else if (dir == Direction.Left)
                    {
                        newFace = 1; // Back face
                        nx = GridSize - 1;
                        ny = from.Y;
                        targetRotY += MathHelper.PiOver2;
                    }
                    else if (dir == Direction.Up)
                    {
                        newFace = 4; // Top face
                        nx = from.X;
                        ny = 0;
                        targetRotX += MathHelper.PiOver2;
                    }
                    else if (dir == Direction.Down)
                    {
                        newFace = 5; // Bottom face
                        nx = from.X;
                        ny = GridSize - 1;
                        targetRotX -= MathHelper.PiOver2;
                    }
                    break;

                case 1: // Back face
                    if (dir == Direction.Right)
                    {
                        newFace = 3; // Left face
                        nx = 0;
                        ny = from.Y;
                        targetRotY -= MathHelper.PiOver2;
                    }
                    else if (dir == Direction.Left)
                    {
                        newFace = 2; // Right face
                        nx = GridSize - 1;
                        ny = from.Y;
                        targetRotY += MathHelper.PiOver2;
                    }
                    else if (dir == Direction.Up)
                    {
                        newFace = 4; // Top face
                        nx = from.X;
                        ny = 0;
                        targetRotX += MathHelper.PiOver2;
                    }
                    else if (dir == Direction.Down)
                    {
                        newFace = 5; // Bottom face
                        nx = from.X;
                        ny = GridSize - 1;
                        targetRotX -= MathHelper.PiOver2;
                    }
                    break;

                case 4: // Top face
                    if (dir == Direction.Right)
                    {
                        newFace = 2; // Right face
                        nx = 0;
                        ny = from.Y;
                        targetRotY -= MathHelper.PiOver2;
                    }
                    else if (dir == Direction.Left)
                    {
                        newFace = 3; // Left face
                        nx = GridSize - 1;
                        ny = from.Y;
                        targetRotY += MathHelper.PiOver2;
                    }
                    else if (dir == Direction.Up)
                    {
                        newFace = 1; // Back face
                        nx = from.X;
                        ny = 0;
                        targetRotX += MathHelper.PiOver2;
                    }
                    else if (dir == Direction.Down)
                    {
                        newFace = 0; // Front face
                        nx = from.X;
                        ny = GridSize - 1;
                        targetRotX -= MathHelper.PiOver2;
                    }
                    break;

                case 5: // Bottom face
                    if (dir == Direction.Right)
                    {
                        newFace = 2; // Right face
                        nx = 0;
                        ny = from.Y;
                        targetRotY -= MathHelper.PiOver2;
                    }
                    else if (dir == Direction.Left)
                    {
                        newFace = 3; // Left face
                        nx = GridSize - 1;
                        ny = from.Y;
                        targetRotY += MathHelper.PiOver2;
                    }
                    else if (dir == Direction.Up)
                    {
                        newFace = 0; // Front face
                        nx = from.X;
                        ny = 0;
                        targetRotX += MathHelper.PiOver2;
                    }
                    else if (dir == Direction.Down)
                    {
                        newFace = 1; // Back face
                        nx = from.X;
                        ny = GridSize - 1;
                        targetRotX -= MathHelper.PiOver2;
                    }
                    break;
            }

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

            // Rotate the CUBE, not the camera
            _effect.World = Matrix.CreateRotationX(currentRotX) * Matrix.CreateRotationY(currentRotY);
            _effect.View = _view;
            _effect.Projection = _projection;

            // Draw faces
            for (int f = 0; f < 6; f++)
            {
                DrawFaceQuad(f, _faceColors[f]);
                DrawFaceGrid(f, Color.Black);
            }

            // Draw snake
            foreach (var seg in _snake)
                DrawCellOnFace(seg, Color.DarkGreen);

            // Draw food
            DrawCellOnFace(_food, Color.Red);

            // Draw wireframe
            GraphicsDevice.RasterizerState = new RasterizerState { CullMode = CullMode.None };
            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GraphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, _cubeWire, 0, _cubeWire.Length / 2);
            }

            base.Draw(gameTime);
        }

        private void DrawFaceQuad(int faceIndex, Color color)
        {
            var verts = FaceQuadVertices(faceIndex, color);
            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, verts, 0, 2);
            }
        }

        private void DrawFaceGrid(int faceIndex, Color color)
        {
            for (int i = 1; i < GridSize; i++)
            {
                var vline = FaceLineVertices(faceIndex, true, i, color);
                foreach (var pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, vline, 0, 2);
                }
                var hline = FaceLineVertices(faceIndex, false, i, color);
                foreach (var pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, hline, 0, 2);
                }
            }
        }

        private void DrawCellOnFace(Cell cell, Color color)
        {
            var verts = FaceCellVertices(cell, color);
            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, verts, 0, 2);
            }
        }

        private VertexPositionColor[] FaceQuadVertices(int faceIndex, Color c)
        {
            Vector3 n = FaceNormal(faceIndex);
            Vector3 right = FaceRight(faceIndex);
            Vector3 up = FaceUp(faceIndex);

            float faceHalf = CubeSize / 2f;
            Vector3 bl = n * Half + (-right - up) * faceHalf;
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

        private VertexPositionColor[] FaceLineVertices(int faceIndex, bool isVertical, int index, Color c)
        {
            Vector3 n = FaceNormal(faceIndex);
            Vector3 right = FaceRight(faceIndex);
            Vector3 up = FaceUp(faceIndex);

            float cellWorld = CubeSize / GridSize;
            float halfLine = 0.01f;
            float coord = -Half + index * cellWorld;

            if (isVertical)
            {
                Vector3 center = n * Half + right * coord;
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

        private VertexPositionColor[] FaceCellVertices(Cell cell, Color color)
        {
            Vector3 n = FaceNormal(cell.Face);
            Vector3 right = FaceRight(cell.Face);
            Vector3 up = FaceUp(cell.Face);

            float cellWorld = CubeSize / GridSize;
            float cx = -Half + (cell.X + 0.5f) * cellWorld;
            float cy = -Half + (cell.Y + 0.5f) * cellWorld;
            Vector3 center = n * Half + right * cx + up * cy;

            float h = cellWorld * 0.45f;
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

        private Vector3 FaceNormal(int f) => f switch
        {
            0 => new Vector3(0, 0, 1),
            1 => new Vector3(0, 0, -1),
            2 => new Vector3(1, 0, 0),
            3 => new Vector3(-1, 0, 0),
            4 => new Vector3(0, 1, 0),
            5 => new Vector3(0, -1, 0),
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

        private readonly record struct Cell(int Face, int X, int Y)
        {
            public Cell Move(Direction dir)
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