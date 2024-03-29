﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using MelonLoader;
using UnityEngine;
using HarmonyLib;
using Steamworks;
using System.IO;

// patch PB saving to never save when TASing enabled

namespace NeonTAS {
    public class InputBuffer {
        public class InputTick {
            public int idx;

            public float? x_move_axis;
            public float? y_move_axis;
            
            public float? face_direction; // -360 -> +360
            public float? face_angle; // -90 -> 90

            public bool jump;
            public bool fire;
            public bool discard;
            public bool swap;

            public bool restart;
            public bool pause_and_unhook;

            public InputTick() {
                idx = -1;
                x_move_axis = null;
                y_move_axis = null;
                face_direction = null;
                face_angle = null;

                jump = swap = fire = discard = restart = pause_and_unhook = false;
            }

            public static InputTick FromString(string s) {
                InputTick ret = new InputTick();

                // format is frame>x,y,dir,angle|JFDSRP
                // split on > to get (frame, bunchashit)
                // split bunchashit on | to get (numbers, actions)
                // split numbers on , to get ["" or number x4]
                // do "find" on actions to set bools :)

                string[] parts;
                string frame;
                string inputs;
                string[] movements;
                string actions = "";

                parts = s.Split('>');
                if (parts.Length == 2) {
                    frame = parts[0];
                    inputs = parts[1];
                } else {
                    return null; // invalid, skip frame.
                }

                if (frame != "") ret.idx = int.Parse(frame);
                if (inputs != "") {
                    parts = inputs.Split('|');
                } else {
                    return ret; // just "frame>". Indicates same wasd / no buttons held 
                }

                // guaranteed sed: if inputs were "" we would have returned, and would have nothing to split
                movements = parts[0].Split(',');
                if (parts.Length == 2) {
                    actions = parts[1];
                }


                for (int i = 0; i < movements.Length; ++i) {
                    if (movements[i] != "") {
                        float num = float.Parse(movements[i]);
                        switch (i) {
                            case 0:
                                ret.x_move_axis = num;
                                break;
                            case 1:
                                ret.y_move_axis = num;
                                break;
                            case 2:
                                ret.face_direction = num;
                                break;
                            case 3:
                                ret.face_angle = num;
                                break;
                        }
                    }
                }

                if (actions.Contains("J")) {
                    ret.jump = true;
                }
                if (actions.Contains("F")) {
                    ret.fire = true;
                }
                if (actions.Contains("D")) {
                    ret.discard = true;
                }
                if (actions.Contains("S")) {
                    ret.swap = true;
                }
                if (actions.Contains("R")) {
                    ret.restart = true;
                }
                if (actions.Contains("P")) {
                    ret.pause_and_unhook = true;
                }

                return ret;
            }
            public override string ToString() {
                if (x_move_axis == null && y_move_axis == null && face_direction == null && face_angle == null &&
                    !(jump || fire || discard || swap || restart || pause_and_unhook)) {
                    return "";
                }
                // print frame#, padded to 5 digits for uniformity :)
                string ret = idx.ToString("D5") + ">";
                if (x_move_axis != null) {
                    ret += x_move_axis.ToString();
                }
                ret += ",";
                if (y_move_axis != null) {
                    ret += y_move_axis.ToString();
                }
                ret += ",";
                if (face_direction != null) {
                    ret += face_direction.ToString();
                }
                ret += ",";
                if (face_angle != null) {
                    ret += face_angle.ToString();
                }
                ret += "|";
                if (jump) ret += "J";
                    else ret += " ";
                if (fire) ret += "F";
                    else ret += " ";
                if (discard) ret += "D";
                    else ret += " ";
                if (swap) ret += "S";
                 else ret += " ";
                if (restart) ret += "R";
                    else ret += " ";
                if (pause_and_unhook) ret += "P";
                    else ret += " ";
                return ret;
            }
        }

        private InputTick[] frames;
        // 60ticks/s -> 3600 ticks/minute -> 18,000 ticks for 5 minutes should be fine :)
        private static int size = 18000;
        private int max_frame_idx;
        public InputBuffer() {
            frames = new InputTick[size];
            max_frame_idx = -1;
        }

        public int NumFrames() {
            // we are 0-indexed, this is for use in for loops
            return max_frame_idx + 1;
        }

        public bool Write(InputTick t) {
            if (t == null) return false;
            if (t.idx >= 0 && t.idx < size) {
                if (t.idx > max_frame_idx) {
                    max_frame_idx = t.idx;
                }
                this.frames[t.idx] = t;
                return true;
            }
            return false;
        }

        public InputTick Get(int idx) {
            if (idx >=0 && idx < size) {
                return frames[idx];
            }
            return null;
        }

        public void ParseString(string inputs) {
            foreach (var line in inputs.Split('\n')) {
                InputTick tick = InputTick.FromString(line);
                if (tick != null) {
                    this.Write(tick);
                }
            }
        }
    }

    public class TAS_tools : MelonMod {
        public static MelonPreferences_Category tas_config;

        public static MelonPreferences_Entry<bool> replay_enabled;
        public static MelonPreferences_Entry<string> replay_filename;
        public static MelonPreferences_Entry<bool> copy_replay_filename_to_clipboard;
        public static MelonPreferences_Entry<bool> recording_enabled;

        public static MelonPreferences_Entry<float> timescale;

        public static MelonPreferences_Entry<bool> debug_text;
        public static MelonPreferences_Entry<int> x_offset;
        public static MelonPreferences_Entry<int> y_offset;
        public static MelonPreferences_Entry<int> font_size;

        public static bool level_hook_methods_patched = false;
        public static bool input_write_methods_patched = false;

        class DisablePBUpdating_Patch {
            [HarmonyPatch(typeof(LevelStats), "UpdateTimeMicroseconds")]
            [HarmonyPrefix]
            static bool SkipUpdatingPb(LevelStats __instance, long newTime) {
                __instance._timeLastMicroseconds = newTime;
                return false;
            }
        }

        static HarmonyLib.Harmony pb_disabling_instance;
        static HarmonyLib.Harmony tick_hook_instance;

        public override void OnApplicationLateStart() {
            GameDataManager.powerPrefs.dontUploadToLeaderboard = true;
            pb_disabling_instance = new HarmonyLib.Harmony("PB Blocking");
            pb_disabling_instance.PatchAll(typeof(DisablePBUpdating_Patch));
            tick_hook_instance = new HarmonyLib.Harmony("tick hooks");
            tick_hook_instance.PatchAll(typeof(PlayerTickYeehawPatch));

            tas_config = MelonPreferences.CreateCategory("TAS Tools");
            // replaying requires level start/finish patches that hook/unhook input method patches
            // recording requires level start/finish patches that just handle recording state

            recording_enabled = tas_config.CreateEntry("Recording Enabled (if not replaying)", false);
            copy_replay_filename_to_clipboard = tas_config.CreateEntry("Copy replay filename to clipboard", false);

            replay_enabled = tas_config.CreateEntry("Replaying Enabled", false);
            replay_filename = tas_config.CreateEntry("Replay filename to load", "active.TAS");

            timescale = tas_config.CreateEntry("Timescale", 1f);

            debug_text = tas_config.CreateEntry("Debug text", false);
            x_offset = tas_config.CreateEntry("X Offset", 30);
            y_offset = tas_config.CreateEntry("Y Offset", 30);
            font_size = tas_config.CreateEntry("Font Size", 20);

            if (replay_enabled.Value || recording_enabled.Value) {
                PatchLevelMethods();
            }
        }
        public override void OnPreferencesSaved() {
            if (level_hook_methods_patched && !(replay_enabled.Value || recording_enabled.Value)) {
                UnpatchLevelMethods();
            } else if (replay_enabled.Value || recording_enabled.Value) {
                PatchLevelMethods();
            }
            if (input_write_methods_patched && !replay_enabled.Value) {
                UnpatchInputMethods();
            }
        }

        private void PatchLevelMethods() {
            // Apply patches to level start & end that will apply & remove input hook patches
            if (!level_hook_methods_patched) {
                HarmonyLib.Harmony harmony = this.HarmonyInstance;
                harmony.PatchAll(typeof(LevelStart_MechControllerInputHook_Patch));
                harmony.PatchAll(typeof(LevelEnd_Patches));
                level_hook_methods_patched = true;
            }
        }

        private void UnpatchLevelMethods() {
            // Remove patches from level start & end
            if (level_hook_methods_patched) {
                HarmonyLib.Harmony harmony = this.HarmonyInstance;
                harmony.UnpatchSelf();
                level_hook_methods_patched = false;
            }
            // also clean up input patches; don't leave those lingering!
            UnpatchInputMethods();
        }

        class LevelStart_MechControllerInputHook_Patch {
            [HarmonyPatch(typeof(MechController), "ForceSetup")]
            [HarmonyPrefix]
            static void PatchInputHooks() {
                if (replay_enabled.Value) {
                    PatchInputMethods();
                    OnLevelStart_ReplaySetup();
                } else if (recording_enabled.Value) {
                    OnLevelStart_RecordSetup();
                }
                
            }
        }

        class LevelEnd_Patches {
            [HarmonyPatch(typeof(Game), "OnLevelWin")]
            [HarmonyPrefix]
            static void OnWin_Unhook_path() {
                if (replay_enabled.Value && !delayed_record) {
                    UnpatchInputMethods();
                } else if (recording_enabled.Value) {
                    OnLevelWin_RecordingFinish();
                }
            }

            [HarmonyPatch(typeof(FirstPersonDrifter), "OnPlayerDie")]
            [HarmonyPrefix]
            static void OnDeath_Unhook_Patch() {
                if (replay_enabled.Value && !delayed_record) {
                    UnpatchInputMethods();
                } else if (recording_enabled.Value) {
                    OnLevelWin_RecordingFinish();
                }
            }
        }

        static HarmonyLib.Harmony input_patches_instance = new HarmonyLib.Harmony("Inputs");

        private static void PatchInputMethods() {
            if (!input_write_methods_patched) {
                input_patches_instance.PatchAll(typeof(GameInputPatches));
                input_write_methods_patched = true;
            }
        }

        private static void UnpatchInputMethods() {
            if (input_write_methods_patched) {
                input_patches_instance.UnpatchSelf();
                input_write_methods_patched = false;
            }
        }

        class PlayerTickYeehawPatch {
            // unilaterally applied hook. Use with care, gate calls appropriately.
            [HarmonyPatch(typeof(FirstPersonDrifter), "UpdateVelocity")]
            [HarmonyPrefix]
            static void UpdateInputValuesForTick() {
                // note: could potentially grab the currentVelocity before/after each step and log that in the recording file
                // || position etc. idk. would only be handy for debug shit.
                // gate the input feed actions on if the input methods are patched
                if (replay_enabled.Value && input_write_methods_patched) {
                    ReplayInputs();
                } else if ((!replay_enabled.Value || delayed_record) && recording_enabled.Value) {
                    RecordInputs();
                }
                if (replay_enabled.Value || recording_enabled.Value) {
                    ++frame_idx;
                }
            }
        }

        class GameInputPatches {
            [HarmonyPatch(typeof(GameInput), "GetAxis")]
            [HarmonyPrefix]
            static bool InjectGetAxisInputs(ref float __result, GameInput.GameActions axis, GameInput.InputType inputType = GameInput.InputType.Game) {
                if (inputType != GameInput.InputType.Game) {
                    return true;
                }
                switch (axis) {
                    case GameInput.GameActions.MoveHorizontal: {
                            __result = frame_inputX;
                            return false;
                        }
                    case GameInput.GameActions.MoveVertical: {
                            __result = frame_inputY;
                            return false;
                        }
                    case GameInput.GameActions.SwapCard: {
                            __result = frame_swap_card ? 1f : 0f;
                            return false;
                        }
                    default: return true;
                }
            }

            [HarmonyPatch(typeof(GameInput), "GetAxisRaw")]
            [HarmonyPrefix]
            static bool DisableMouseInputs(ref float __result, GameInput.GameActions axis, GameInput.InputType inputType = GameInput.InputType.Game) {
                if (inputType != GameInput.InputType.Game) {
                    return true;
                }
                switch (axis) {
                    case GameInput.GameActions.LookHorizontal: {
                            __result = 0;
                            return false;
                        }
                    case GameInput.GameActions.LookVertical: {
                            __result = 0;
                            return false;
                        }
                    default: return true;
                }
            }

            [HarmonyPatch(typeof(GameInput), "GetButton")]
            [HarmonyPrefix]
            static bool InjectButtonInputs(ref bool __result, GameInput.GameActions button, GameInput.InputType inputType = GameInput.InputType.Game) {
                if (inputType != GameInput.InputType.Game) {
                    return true;
                }
                switch (button) {
                    case GameInput.GameActions.FireCard: {
                            __result = frame_fire_pressed;
                            return false;
                        }
                    case GameInput.GameActions.FireCardAlt: {
                            __result = frame_discard_pressed;
                            return false;
                        }
                    case GameInput.GameActions.Jump: {
                            __result = frame_jump_pressed;
                            return false;
                        }
                    case GameInput.GameActions.Restart: {
                            __result = frame_restart_pressed;
                            return false;
                        }
                    default: return true;
                }
            }

            [HarmonyPatch(typeof(GameInput), "GetButtonDown")]
            [HarmonyPrefix]
            // returns True on first frame pressed, but not if pressed last frame
            static bool InjectButtonDownInputs(ref bool __result, GameInput.GameActions button, GameInput.InputType inputType = GameInput.InputType.Game) {
                if (inputType != GameInput.InputType.Game) {
                    return true;
                }
                switch (button) {
                    case GameInput.GameActions.FireCard: {
                            __result = frame_fire_pressed && !last_frame_fire_pressed;
                            return false;
                        }
                    case GameInput.GameActions.FireCardAlt: {
                            __result = frame_discard_pressed && !last_frame_discard_pressed;
                            return false;
                        }
                    case GameInput.GameActions.Jump: {
                            __result = frame_jump_pressed && !last_frame_jump_pressed;
                            return false;
                        }
                    case GameInput.GameActions.Restart: {
                            __result = frame_restart_pressed && !last_frame_restart_pressed;
                            return false;
                        }
                    default: return true;
                }
            }

            [HarmonyPatch(typeof(GameInput), "GetButtonUp")]
            [HarmonyPrefix]
            static bool InjectButtonUpInputs(ref bool __result, GameInput.GameActions button, GameInput.InputType inputType = GameInput.InputType.Game) {
                if (inputType != GameInput.InputType.Game) {
                    return true;
                }
                switch (button) {
                    case GameInput.GameActions.FireCard: {
                            __result = !frame_fire_pressed && last_frame_fire_pressed;
                            return false;
                        }
                    case GameInput.GameActions.FireCardAlt: {
                            __result = !frame_discard_pressed && last_frame_discard_pressed;
                            return false;
                        }
                    case GameInput.GameActions.Jump: {
                            __result = !frame_jump_pressed && last_frame_jump_pressed;
                            return false;
                        }
                    case GameInput.GameActions.Restart: {
                            __result = !frame_restart_pressed && last_frame_restart_pressed;
                            return false;
                        }
                    default: return true;
                }
            }
        }

        public static InputBuffer buffer = null;
        public static int frame_idx = 0;

        public static float frame_inputX = 0f;
        public static float frame_inputY = 0f;
        public static float frame_lookX = 0f;
        public static float frame_lookY = 0f;

        public static bool frame_jump_pressed;
        public static bool last_frame_jump_pressed;

        public static bool frame_fire_pressed;
        public static bool last_frame_fire_pressed;

        public static bool frame_discard_pressed;
        public static bool last_frame_discard_pressed;

        public static bool frame_restart_pressed;
        public static bool last_frame_restart_pressed;

        public static bool frame_swap_card;

        public static bool delayed_record = false;

        public static void setJump(bool v) {
            last_frame_jump_pressed = frame_jump_pressed;
            frame_jump_pressed = v;
        }

        public static void setFire(bool v) {
            last_frame_fire_pressed = frame_fire_pressed;
            frame_fire_pressed = v;
        }

        public static void setDiscard(bool v) {
            last_frame_discard_pressed = frame_discard_pressed;
            frame_discard_pressed = v;
        }

        public static void setRestart(bool v) {
            last_frame_restart_pressed = frame_restart_pressed;
            frame_restart_pressed = v;
        }

        public static void setRotation(float? x, float? y) {
            if (x.HasValue) {
                float clamped_x = x.Value % 360;
                RM.drifter.mouseLookX.SetRotationX(clamped_x);

            }
            if (y.HasValue) {
                float clamped_y = y.Value % 360;
                RM.drifter.mouseLookY.SetRotationY(clamped_y);
            }
        }

        public static void OnLevelStart_RecordSetup() {
            frame_idx = 0;
            last_tick = null;
            buffer = new InputBuffer();
        }

        public static void OnLevelStart_ReplaySetup() {
            frame_idx = 0;
            buffer = new InputBuffer();
            last_tick = null;

            string path = GetFilePath();
            string filename = "active.TAS";
            if (replay_filename.Value != "") {
                filename = replay_filename.Value;
            }
            path = path + Path.DirectorySeparatorChar.ToString() + filename;

            if (File.Exists(path)) {
                string inputs = File.ReadAllText(path);
                MelonLogger.Msg("Read inputs from " + path + "\n");

                buffer.ParseString(inputs);
                delayed_record = false;
            } else {
                MelonLogger.Msg("No file found at [" + path + "]. Unhooking input replaying.\n");
                UnpatchInputMethods();
                delayed_record = true;;
            }
        }

        public static void OnLevelWin_RecordingFinish() {
            // bad code.
            string inputs = "";
            for (int i = 0; i < buffer.NumFrames(); i++) {
                InputBuffer.InputTick tick = buffer.Get(i);
                if (tick != null) inputs += tick.ToString() + "\n";
            }
            if (inputs != "") {
                WriteStringToFile(inputs);
            } // surely this is never false
            buffer = null;
        }

        public static string GetFilePath() {
            string text = "NOUSERNAME";
            if (Application.isPlaying && SteamManager.Initialized) {
                try {
                    SteamUser.BLoggedOn();
                    text = SteamUser.GetSteamID().ToString();
                } catch {
                }
            }
            string filePath = string.Concat(new string[]
            {
                Path.GetFullPath(Application.persistentDataPath),
                Path.DirectorySeparatorChar.ToString(),
                text,
                Path.DirectorySeparatorChar.ToString(),
                "Ghosts",
                Path.DirectorySeparatorChar.ToString(),
                Singleton<Game>.Instance.GetCurrentLevel().levelID
            });
            if (!Directory.Exists(filePath)) {
                Directory.CreateDirectory(filePath);
            }
            return filePath;
        }

        public static void WriteStringToFile(string inputs) {
            string path = GetFilePath();
            string timestamp = DateTime.Now.ToString("yyyy_MM_dd HH_mm_ss.ffff");
            string filename = (buffer.NumFrames() + 1).ToString("D5") + "_" + timestamp + ".TAS";
            path = path + Path.DirectorySeparatorChar.ToString() + filename;
            // [ghosts folder]/[level name]/[frame count]_[timestamp].TAS
            File.WriteAllText(path, inputs);
            // copy filename to clipboard.
            if (copy_replay_filename_to_clipboard.Value) {
                GUIUtility.systemCopyBuffer = filename;
            }
            MelonLogger.Msg("Recorded inputs to " + path + "\n");
        }

        static InputBuffer.InputTick last_tick = null;

        public static void ReplayInputs() {
            if (buffer == null) return;
            InputBuffer.InputTick tick = buffer.Get(frame_idx); // nullable!
            if (tick == null) {
                // do not adjust x_move_axis or y_move_axis
                setRotation(frame_lookX, frame_lookY);
                setJump(false);
                setFire(false);
                setDiscard(false);
                frame_swap_card = false;
                setRestart(false);
            } else {
                if (tick.x_move_axis != null) {
                    frame_inputX = tick.x_move_axis.Value;
                }
                if (tick.y_move_axis != null) {
                    frame_inputY = tick.y_move_axis.Value;
                }
                if (tick.face_direction != null) {
                    frame_lookX = tick.face_direction.Value;
                }
                if (tick.face_angle != null) {
                    frame_lookY = tick.face_angle.Value;
                }
                // setrotation every frame, so that if we somehow miss a frame we still try and set correctly again
                // is that really how it works or was it just that our flick lands a frame too early sometimes :think:
                setRotation(frame_lookX, frame_lookY);

                setJump(tick.jump);
                setFire(tick.fire);
                setDiscard(tick.discard);
                frame_swap_card = tick.swap;
                setRestart(tick.restart);
                if (tick.pause_and_unhook) {
                    // make this a proper func? mayhaps?
                    UnpatchInputMethods();
                    MainMenu.Instance().PauseGame(true, true, true);
                    delayed_record = true;
                    MelonLogger.Msg("Unpatched methods and handed back control *on* frame " + frame_idx.ToString());
                }
            }
            last_tick = tick;
        }

        public static void RecordInputs() {
            if (buffer == null) return;
            InputBuffer.InputTick tick = new InputBuffer.InputTick();
            InputBuffer.InputTick last_tick = buffer.Get(frame_idx - 1); // nullable!
            tick.idx = frame_idx;
            tick.x_move_axis = Singleton<GameInput>.Instance.GetAxis(GameInput.GameActions.MoveHorizontal, GameInput.InputType.Game);
            tick.y_move_axis = Singleton<GameInput>.Instance.GetAxis(GameInput.GameActions.MoveVertical, GameInput.InputType.Game);

            if (RM.drifter != null) {
                // i don't think this can happen, but may as well just in case?
                tick.face_direction = RM.drifter.mouseLookX.RotationX;
                tick.face_angle = RM.drifter.mouseLookY.RotationY;
            }

            if (last_tick != null) {
                if (last_tick.x_move_axis == tick.x_move_axis) tick.x_move_axis = null;
                if (last_tick.y_move_axis == tick.y_move_axis) tick.y_move_axis = null;
                if (last_tick.face_direction == tick.face_direction) tick.face_direction = null;
                if (last_tick.face_angle == tick.face_angle) tick.face_angle = null;
            }

            tick.fire = Singleton<GameInput>.Instance.GetButton(GameInput.GameActions.FireCard, GameInput.InputType.Game);
            tick.discard = Singleton<GameInput>.Instance.GetButton(GameInput.GameActions.FireCardAlt, GameInput.InputType.Game);
            tick.jump = Singleton<GameInput>.Instance.GetButton(GameInput.GameActions.Jump, GameInput.InputType.Game);

            tick.swap = Mathf.Abs(Singleton<GameInput>.Instance.GetAxis(GameInput.GameActions.SwapCard, GameInput.InputType.Game)) > 0.01f;
            tick.restart = false;
            tick.pause_and_unhook = false;
            buffer.Write(tick);
            last_tick = tick;
        }

        public override void OnUpdate() {
            // note: when timescale fuckery is going on, stuff starts happening when waiting for level start.
            // it resets on level start, but probably need to gate this better (e.g. not adjust timescale if timescale == 0)
            if (RM.time != null && RM.time.GetTargetTimeScale() != 0f) {
                RM.time.SetTargetTimescale(timescale.Value);
                Application.targetFrameRate = (int) (60f * timescale.Value);
            }
        }

        // DEBUG SHIT
        public static GUIStyle TASInputStyle() {
            GUIStyle style = new GUIStyle();

            style.fixedHeight = font_size.Value;
            style.fontSize = font_size.Value;

            return style;
        }

        public void DrawText(int x_offset, int y_offset, string s, Color c) {
            GUIStyle style = TASInputStyle();
            style.normal.textColor = c;

            GUIStyle outline_style = TASInputStyle();
            outline_style.normal.textColor = Color.black;
            int outline_strength = 2;

            Rect r = new Rect(x_offset, y_offset, 120, 30);

            for (int i = -outline_strength; i <= outline_strength; ++i) {
                GUI.Label(new Rect(r.x - outline_strength, r.y + i, r.width, r.height), s, outline_style);
                GUI.Label(new Rect(r.x + outline_strength, r.y + i, r.width, r.height), s, outline_style);
            }
            for (int i = -outline_strength + 1; i <= outline_strength - 1; ++i) {
                GUI.Label(new Rect(r.x + i, r.y - outline_strength, r.width, r.height), s, outline_style);
                GUI.Label(new Rect(r.x + i, r.y + outline_strength, r.width, r.height), s, outline_style);
            }
            GUI.Label(r, s, style);
        }

        public string VecToString(Vector3 v) {
            return v.x.ToString("N2") + ", " + v.y.ToString("N2") + ", " + v.z.ToString("N2");
        }

        public override void OnGUI() {
            if (!RM.mechController || !RM.drifter) return;
            int local_y_offset = y_offset.Value;
            DrawText(x_offset.Value, local_y_offset, "Input writes patched: " + input_write_methods_patched.ToString(), Color.magenta);
            local_y_offset += font_size.Value + 2;

            DrawText(x_offset.Value, local_y_offset, "Level Hooks:" + level_hook_methods_patched.ToString(), Color.magenta);
            local_y_offset += font_size.Value + 2;

            DrawText(x_offset.Value, local_y_offset, "Frame #" + frame_idx.ToString(), Color.magenta);
            local_y_offset += font_size.Value + 2;

            if (RM.drifter && RM.drifter.mouseLookX && RM.drifter.mouseLookY) {
                float heading = RM.drifter.mouseLookX.RotationX;
                float elevation = RM.drifter.mouseLookY.RotationY;
                DrawText(x_offset.Value, local_y_offset, heading.ToString("N3") + " @ " + elevation.ToString("N3"), Color.magenta);
                local_y_offset += font_size.Value + 2;
            }

            if (replay_enabled.Value && !delayed_record) {
                DrawText(x_offset.Value, local_y_offset, "Replaying inputs!", Color.magenta);
                local_y_offset += font_size.Value + 2;
            }
            if (recording_enabled.Value) {
                string suffix = "";
                if (delayed_record) suffix = " (because not replaying)";
                DrawText(x_offset.Value, local_y_offset, "Recording!" + suffix, Color.magenta);
                local_y_offset += font_size.Value + 2;
            }

            if (debug_text.Value) {
                if (last_tick != null) {
                    DrawText(x_offset.Value, local_y_offset, last_tick.ToString(), Color.magenta);
                    local_y_offset += font_size.Value + 2;
                }
            }
        }
    }
}
