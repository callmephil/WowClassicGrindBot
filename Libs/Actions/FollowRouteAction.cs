﻿using Libs.GOAP;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Numerics;

namespace Libs.Actions
{
    public class FollowRouteAction : GoapAction
    {
        private double RADIAN = Math.PI * 2;
        private WowProcess wowProcess;
        private readonly List<WowPoint> pointsList;
        private Stack<WowPoint> points=new Stack<WowPoint>();
        private readonly PlayerReader playerReader;
        private readonly IPlayerDirection playerDirection;
        private double lastDistance = 999;
        private DateTime LastActive = DateTime.Now;

        public FollowRouteAction(PlayerReader playerReader, WowProcess wowProcess, IPlayerDirection playerDirection, List<WowPoint> points)
        {
            this.playerReader = playerReader;
            this.wowProcess = wowProcess;
            this.playerDirection = playerDirection;
            this.pointsList = points;

            AddPrecondition(GoapKey.incombat, false);

            RefillPoints();
        }

        private void RefillPoints()
        {
            pointsList.ForEach(p => points.Push(p));
        }

        public override float CostOfPerformingAction { get => 20f; }

        public void Dump(string description)
        {
            var location = new WowPoint(playerReader.XCoord, playerReader.YCoord);
            var distance = DistanceTo(location, points.Peek());
            var heading = new DirectionCalculator().CalculateHeading(location, points.Peek());
            //Debug.WriteLine($"{description}: Point {index}, Distance: {distance} ({lastDistance}), heading: {playerReader.Direction}, best: {heading}");
        }

        private DateTime lastTab = DateTime.Now;

        public override async Task PerformAction()
        {
            await Task.Delay(200);
            //wowProcess.SetKeyState(ConsoleKey.UpArrow, true);

            if (this.playerReader.PlayerBitValues.PlayerInCombat){ return; }

            if ((DateTime.Now - LastActive).TotalSeconds > 10)
            {
                var pointsRemoved = 0;
                while (AdjustNextPointToClosest() && pointsRemoved<5) { pointsRemoved++; };
            }

            LastActive = DateTime.Now;

            // press tab
                if (!this.playerReader.PlayerBitValues.PlayerInCombat && (DateTime.Now - lastTab).TotalMilliseconds > 1100)
            {
                //new PressKeyThread(this.wowProcess, ConsoleKey.Tab);
                this.wowProcess.SetKeyState(ConsoleKey.Tab, true);
                Thread.Sleep(300);
                this.wowProcess.SetKeyState(ConsoleKey.Tab, false);
            }

            var location = new WowPoint(playerReader.XCoord, playerReader.YCoord);
            var distance = DistanceTo(location, points.Peek());
            var heading = new DirectionCalculator().CalculateHeading(location, points.Peek());

            if (lastDistance < distance)
            {
                Dump("Further away");
                playerDirection.SetDirection(heading);
            }
            else if (lastDistance == distance)
            {
                Dump("Stuck");
                // stuck so jump
                wowProcess.SetKeyState(ConsoleKey.UpArrow, true);
                await Task.Delay(100);
                wowProcess.SetKeyState(ConsoleKey.Spacebar, true);
                await Task.Delay(500);
                wowProcess.SetKeyState(ConsoleKey.Spacebar, false);
            }
            else // distance closer
            {
                Dump("Closer");
                //playerDirection.SetDirection(heading);

                var diff1 = Math.Abs(RADIAN + heading - playerReader.Direction) % RADIAN;
                var diff2 = Math.Abs(heading - playerReader.Direction - RADIAN) % RADIAN;

                if (Math.Min(diff1, diff2) > 0.3)
                {
                    Dump("Correcting direction");
                    playerDirection.SetDirection(heading);
                }
            }

            lastDistance = distance;

            if (distance < 4)
            {
                Debug.WriteLine($"Move to next point");
                points.Pop();
                lastDistance = 999;
                if (points.Count == 0)
                {
                    RefillPoints();
                }

                heading = new DirectionCalculator().CalculateHeading(location, points.Peek());
                playerDirection.SetDirection(heading);
            }
        }

        private bool AdjustNextPointToClosest()
        {
            if (points.Count < 2) { return false; }

            var A = points.Pop();
            var B = points.Peek();
            var result = GetClosestPointOnLineSegment(A.Vector2, B.Vector2, new Vector2((float)this.playerReader.XCoord, (float)this.playerReader.YCoord));
            var newPoint = new WowPoint(result.X, result.Y);
            if (DistanceTo(newPoint, points.Peek()) >= 4)
            {
                points.Push(newPoint);
                Debug.WriteLine($"Adjusted resume point");
                return false;
            }
            else
            {
                Debug.WriteLine($"Skipped next point in path");
                // skiped next point
                return true;
            }
        }

        public void Reset()
        {
            wowProcess.SetKeyState(ConsoleKey.UpArrow, false);
        }

        public bool IsDone()
        {
            return false;
        }

        private double DistanceTo(WowPoint l1, WowPoint l2)
        {
            var x = l1.X - l2.X;
            var y = l1.Y - l2.Y;
            x = x * 100;
            y = y * 100;
            var distance = Math.Sqrt((x * x) + (y * y));

            //Debug.WriteLine($"distance:{x} {y} {distance.ToString()}");
            return distance;
        }

        public override void ResetBeforePlanning()
        {
        }

        public override bool IsActionDone()
        {
            return false;
        }

        public override bool CheckIfActionCanRun()
        {
            return true;
        }

        public override bool NeedsToBeInRangeOfTargetToExecute()
        {
            return false;
        }

        public override void Abort()
        {
            wowProcess.SetKeyState(ConsoleKey.UpArrow, false);
        }

        public static Vector2 GetClosestPointOnLineSegment(Vector2 A, Vector2 B, Vector2 P)
        {
            Vector2 AP = P - A;       //Vector from A to P   
            Vector2 AB = B - A;       //Vector from A to B  

            float magnitudeAB = AB.LengthSquared();     //Magnitude of AB vector (it's length squared)     
            float ABAPproduct = Vector2.Dot(AP, AB);    //The DOT product of a_to_p and a_to_b     
            float distance = ABAPproduct / magnitudeAB; //The normalized "distance" from a to your closest point  

            if (distance < 0)     //Check if P projection is over vectorAB     
            {
                return A;

            }
            else if (distance > 1)
            {
                return B;
            }
            else
            {
                return A + AB * distance;
            }
        }
    }
}