using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace AD_Drill
{
    public partial class Program : MyGridProgram
    {
        const string VERSION = "1.0.0";

        /// <summary>
        /// Default names for blocks and groups. Change these if you want to use different names.
        /// </summary>
        const string pistonGroupName = "AD_Drill_Pistons";
        const string drillGroupName = "AD_Drill_Drills";
        const string welderGroupName = "AD_Drill_Welders";
        const string grinderGroupName = "AD_Drill_Grinders";

        const string rotorName = "AD_Drill_Rotor";
        const string connectorNameTop = "AD_Drill_ConnectorTop";
        const string connectorNameBottom = "AD_Drill_ConnectorBottom";

        const string mergeBlockNameTop = "AD_Drill_MergeBlockTop";
        const string mergeBlockNameBottom = "AD_Drill_MergeBlockBottom";

        const string lcdName = "AD_Drill_LCD";

        // === Settings ===
        const float pistonTargetPositionRetract = 1.1f;
        const float pistonTargetPositionExtend = 8.6f;
        const float pistonVelocityExtend = 0.02f; // m/s
        const float pistonVelocityRetract = -0.5f; // m/s
        const float rotorVelocity = 5.0f; // r/s



        // === Internal variables ===
        private List<IMyShipDrill> drills = new List<IMyShipDrill>();
        private List<IMyShipWelder> welders = new List<IMyShipWelder>();
        private List<IMyPistonBase> pistons = new List<IMyPistonBase>();
        private List<IMyShipGrinder> grinders = new List<IMyShipGrinder>();
        private IMyMotorStator rotor;
        private IMyShipConnector connectorTop;
        private IMyShipConnector connectorBottom;
        private IMyShipMergeBlock mergeBlockTop;
        private IMyShipMergeBlock mergeBlockBottom;
        private IMyTextPanel lcd;

        private int currentState;
        private string currentMode;

        public Program()
        {
            Echo("Initializing");
            WriteLCD("Initializing");

            drills = GetBlocks<IMyShipDrill>(drillGroupName);
            welders = GetBlocks<IMyShipWelder>(welderGroupName);
            pistons = GetBlocks<IMyPistonBase>(pistonGroupName);
            grinders = GetBlocks<IMyShipGrinder>(grinderGroupName);
            rotor = GridTerminalSystem.GetBlockWithName(rotorName) as IMyMotorStator;
            connectorTop = GridTerminalSystem.GetBlockWithName(connectorNameTop) as IMyShipConnector;
            connectorBottom = GridTerminalSystem.GetBlockWithName(connectorNameBottom) as IMyShipConnector;
            mergeBlockTop = GridTerminalSystem.GetBlockWithName(mergeBlockNameTop) as IMyShipMergeBlock;
            mergeBlockBottom = GridTerminalSystem.GetBlockWithName(mergeBlockNameBottom) as IMyShipMergeBlock;
            lcd = GridTerminalSystem.GetBlockWithName(lcdName) as IMyTextPanel;


            rotor.TargetVelocityRPM = 0;

            if (!int.TryParse(Storage, out currentState))
            {
                currentState = 0; // Standardwert
                Echo("No cache found. Set state to 0.");
            }
            else
            {
                Echo("Loaded state: " + currentState);
            }

            currentMode = "stop";
            Runtime.UpdateFrequency = UpdateFrequency.None;

            Echo("Initializing finished");
            WriteLCD("Initialization finished\nVersion: " + VERSION);
        }

        public void Save()
        {
            Storage = currentState.ToString();
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if ((updateSource & UpdateType.Trigger) != 0)
            {
                if (argument == "start")
                {
                    Echo("Start Loop...");
                    Runtime.UpdateFrequency = UpdateFrequency.Update100;
                }
                else if (argument == "stop")
                {
                    Echo("Stop Loop...");
                    Runtime.UpdateFrequency = UpdateFrequency.None;
                }
                else if (argument == "resume")
                {
                    Echo("Resume Loop...");
                    Runtime.UpdateFrequency = UpdateFrequency.Update100;
                }
            }


            switch (currentState)
            {
                case 0:
                    WriteLCD("Drill State: " + currentState + "\nStarting drilling sequence...");

                    // Prepare for drilling
                    SetDrills(drills, true);
                    SetWelders(welders, true);

                    rotor.TargetVelocityRPM = rotorVelocity;

                    connectorTop.Connect();

                    MovePistons(pistons, pistonTargetPositionExtend, pistonVelocityExtend);

                    currentState = 1;

                    break;
                case 1:
                    WriteLCD("Drill State: " + currentState + "\nDrilling...");
                    // Wait for pistons to be fully extended
                    if (PistonsAtPosition(pistons, pistonTargetPositionExtend))
                    {
                        // Stop drilling
                        SetDrills(drills, false);
                        SetWelders(welders, false);

                        rotor.TargetVelocityRPM = 0;
                        mergeBlockBottom.Enabled = true;
                        currentState = 2;
                    }

                    break;
                case 2:
                    WriteLCD("Drill State: " + currentState + "\nMerging bottom...");
                    // Check for bottom merging
                    if (mergeBlockBottom.IsConnected && mergeBlockBottom.Enabled)
                    {
                        mergeBlockTop.Enabled = false;
                        connectorTop.Disconnect();
                        connectorBottom.Connect();
                        SetGrinders(grinders, true);

                        MovePistons(pistons, pistonTargetPositionRetract, pistonVelocityRetract);

                        currentState = 3;
                    }
                    else
                    {
                        Echo("Waiting for merge");
                    }

                    break;

                case 3:
                    WriteLCD("Drill State: " + currentState + "\nRetracting...");
                    // Wait for pistons to be fully retracted
                    if (PistonsAtPosition(pistons, pistonTargetPositionRetract))
                    {
                        // Merge
                        mergeBlockTop.Enabled = true;
                        SetGrinders(grinders, false);

                        currentState = 4;
                    }

                    break;
                case 4:
                    WriteLCD("Drill State: " + currentState + "\nMerging top...");
                    if (mergeBlockTop.IsConnected && mergeBlockTop.Enabled)
                    {
                        // Prepare for next drilling cycle
                        mergeBlockBottom.Enabled = false;
                        connectorBottom.Disconnect();
                        currentState = 0;
                    }
                    else
                    {
                        Echo("Waiting for top merge");
                    }

                    break;

            }

            // Save current state
            Storage = currentState.ToString();
        }


        // === HILFSMETHODEN ===
        void SetDrills(List<IMyShipDrill> drills, bool on)
        {
            foreach (var drill in drills)
                drill.Enabled = on;
        }

        void SetWelders(List<IMyShipWelder> welders, bool on)
        {
            foreach (var welder in welders)
            {
                welder.Enabled = on;
            }
        }
        void SetGrinders(List<IMyShipGrinder> grinders, bool on)
        {
            foreach (var grinder in grinders)
            {
                grinder.Enabled = on;
            }
        }

        void MovePistons(List<IMyPistonBase> pistons, float position, float velocity)
        {
            foreach (var piston in pistons)
            {
                if (piston.CurrentPosition < position)
                    piston.Velocity = Math.Abs(velocity); // ausfahren
                else
                    piston.Velocity = -Math.Abs(velocity); // einfahren
            }
        }


        bool PistonsAtPosition(List<IMyPistonBase> pistons, float position)
        {
            return pistons.All(p => Math.Abs(p.CurrentPosition - position) < 0.05f);
        }

        List<T> GetBlocks<T>(string groupName) where T : class, IMyTerminalBlock
        {
            var group = GridTerminalSystem.GetBlockGroupWithName(groupName);
            var blocks = new List<IMyTerminalBlock>();
            group?.GetBlocksOfType<T>(blocks);
            return blocks.ConvertAll(b => b as T);
        }


        void WriteLCD(string displayText)
        {
            if (lcd != null)
            {
                lcd.ContentType = ContentType.TEXT_AND_IMAGE;
                lcd.WriteText(displayText);
            }
        }

    }
}
