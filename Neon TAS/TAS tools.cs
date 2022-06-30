using System;
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
            public float? face_angle; // -85 -> 85

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

                if (frame != "")
                    ret.idx = int.Parse(frame);
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

        }
    }

    public class TAS_tools : MelonMod {
        public static MelonPreferences_Category tas_config;

        public static MelonPreferences_Entry<bool> replay_enabled;
        public static MelonPreferences_Entry<bool> recording_enabled;
        public static MelonPreferences_Entry<string> recording_path;
        public static MelonPreferences_Entry<bool> debug_text;
        public static MelonPreferences_Entry<int> x_offset;
        public static MelonPreferences_Entry<int> y_offset;
        public static MelonPreferences_Entry<int> font_size;

        public static bool prior_dont_upload_val;
        public static bool level_hook_methods_patched = false;
        public static bool input_write_methods_patched = false;

        public override void OnApplicationStart() {
            GameDataManager.powerPrefs.dontUploadToLeaderboard = true;
            tas_config = MelonPreferences.CreateCategory("TAS Tools");

            // replaying requires level start/finish patches that hook/unhook input method patches
            // recording requires level start/finish patches that just handle recording state
            // :thinking:
            // doing a bunch of Shit around which hooks to apply sucks. let's just have the hooks figure out which one to do :)

            replay_enabled = tas_config.CreateEntry("Replaying Enabled", false);
            recording_enabled = tas_config.CreateEntry("Recording Enabled (if not replaying)", false);
            recording_path = tas_config.CreateEntry("Path to save inputs to", "fuck, man, idk.");

            debug_text = tas_config.CreateEntry("Debug text", false);
            x_offset = tas_config.CreateEntry("X Offset", 30);
            y_offset = tas_config.CreateEntry("Y Offset", 30);
            font_size = tas_config.CreateEntry("Font Size", 20);

            if (replay_enabled.Value || recording_enabled.Value) {
                PatchLevelMethods();
            }
        }

        public override void OnPreferencesSaved() {
            // if (patched) && !(replay || record)
            if (level_hook_methods_patched && !(replay_enabled.Value || recording_enabled.Value)) {
                UnpatchLevelMethods();
            } else if (replay_enabled.Value || recording_enabled.Value) {
                PatchLevelMethods();
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
                if (replay_enabled.Value) {
                    UnpatchInputMethods();
                } else if (recording_enabled.Value) {
                    OnLevelWin_RecordingFinish();
                }
            }

            [HarmonyPatch(typeof(FirstPersonDrifter), "OnPlayerDie")]
            [HarmonyPrefix]
            static void OnDeath_Unhook_Patch() {
                UnpatchInputMethods();
            }
        }

        static HarmonyLib.Harmony input_patches_instance;

        private static void PatchInputMethods() {
            if (!input_write_methods_patched) {
                input_patches_instance = new HarmonyLib.Harmony("Inputs");
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

        public static InputBuffer buffer;
        public static int frame_idx = 0;

        public static float frame_inputX = 0f;
        public static float frame_inputY = 0f;

        public static bool frame_jump_pressed;
        public static bool last_frame_jump_pressed;

        public static bool frame_fire_pressed;
        public static bool last_frame_fire_pressed;

        public static bool frame_discard_pressed;
        public static bool last_frame_discard_pressed;

        public static bool frame_restart_pressed;
        public static bool last_frame_restart_pressed;

        public static bool frame_swap_card;

        public void setJump(bool v) {
            last_frame_jump_pressed = frame_jump_pressed;
            frame_jump_pressed = v;
        }

        public void setFire(bool v) {
            last_frame_fire_pressed = frame_fire_pressed;
            frame_fire_pressed = v;
        }

        public void setDiscard(bool v) {
            last_frame_discard_pressed = frame_discard_pressed;
            frame_discard_pressed = v;
        }

        public void setRestart(bool v) {
            last_frame_restart_pressed = frame_restart_pressed;
            frame_restart_pressed = v;
        }

        public void setRotation(float? x, float? y) {
            if (x.HasValue) {
                float clamped_x = Mathf.Clamp(x.Value, -360, 360);
                RM.drifter.mouseLookX.SetRotationX(clamped_x);

            }
            if (y.HasValue) {
                float clamped_y = Mathf.Clamp(y.Value, -85, 85);
                RM.drifter.mouseLookY.SetRotationY(clamped_y);
            }
        }

        // need to make a thicc array to record inputs into
        // 60tps, 5m would be 300*60->18000 input frames :think:

        public static void OnLevelStart_RecordSetup() {
            frame_idx = 0;
            buffer = new InputBuffer();
        }

        public static void OnLevelStart_ReplaySetup() {
            frame_idx = 0;
            buffer = new InputBuffer();
            string path = GetFilePath();
            string filename = "active.TAS";
            path = path + Path.DirectorySeparatorChar.ToString() + filename;
            string inputs = File.ReadAllText(path);
            Console.Write("Read inputs from " + path);

            buffer.ParseString(inputs);
        }

        public static void OnLevelWin_RecordingFinish() {
            string inputs = "";
            for (int i = 0; i < buffer.NumFrames(); i++) {
                InputBuffer.InputTick tick = buffer.Get(i);
                if (tick != null && tick.ToString() != "") {
                    inputs += tick.ToString() + "\n";
                }
            }
            if (inputs != "") {
                WriteStringToFile(inputs);
            } // surely this is never false
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
            Console.Write("Recorded inputs to " + path);
        }

        public override void OnFixedUpdate() {
            // just gate the input feed actions on if the input methods are patched
            if (replay_enabled.Value && input_write_methods_patched) {
                InputBuffer.InputTick tick = buffer.Get(frame_idx);
                if (tick.x_move_axis != null) {
                    frame_inputX = tick.x_move_axis.Value;
                }
                if (tick.y_move_axis != null) {
                    frame_inputY = tick.y_move_axis.Value;
                }
                if (tick.face_direction != null || tick.face_angle != null) {
                    setRotation(tick.face_direction, tick.face_angle);
                }

                setJump(tick.jump);
                setFire(tick.fire);
                setDiscard(tick.discard);
                frame_swap_card = tick.swap;
                setRestart(tick.restart);
                if (tick.pause_and_unhook) {
                    UnpatchInputMethods();
                    MainMenu.Instance().PauseGame(true, true, true);
                }
                // is this really all i need to do :think:
            } else if (recording_enabled.Value) {
                InputBuffer.InputTick tick = new InputBuffer.InputTick();
                InputBuffer.InputTick last_tick= buffer.Get(frame_idx - 1); // nullable!
                tick.idx = frame_idx;
                tick.x_move_axis = Singleton<GameInput>.Instance.GetAxis(GameInput.GameActions.MoveHorizontal, GameInput.InputType.Game);
                tick.y_move_axis = Singleton<GameInput>.Instance.GetAxis(GameInput.GameActions.MoveVertical,   GameInput.InputType.Game);

                tick.face_direction = RM.drifter.mouseLookX.RotationX;
                tick.face_angle = RM.drifter.mouseLookY.RotationY;
                
                if (last_tick != null) {
                    if (last_tick.x_move_axis == tick.x_move_axis) tick.x_move_axis = null;
                    if (last_tick.y_move_axis == tick.y_move_axis) tick.y_move_axis = null;
                    if (last_tick.face_direction == tick.face_direction) tick.face_direction = null;
                    if (last_tick.face_angle == tick.face_angle) tick.face_angle = null;
                }

                tick.fire = Singleton<GameInput>.Instance.GetButtonDown(GameInput.GameActions.FireCard, GameInput.InputType.Game);
                tick.discard = Singleton<GameInput>.Instance.GetButtonDown(GameInput.GameActions.FireCardAlt, GameInput.InputType.Game);
                tick.jump = Singleton<GameInput>.Instance.GetButtonDown(GameInput.GameActions.Jump, GameInput.InputType.Game);
                tick.swap = Mathf.Abs(Singleton<GameInput>.Instance.GetAxis(GameInput.GameActions.SwapCard, GameInput.InputType.Game)) > 0.01f;
                tick.restart = false;
                tick.pause_and_unhook = false;
                buffer.Write(tick);
            }
            if (replay_enabled.Value || recording_enabled.Value) {
                ++frame_idx;
            }
            // note: should have a keyword/action for 'pause game and hand back input control to user
            // this should also load the 'record inputs' hook / set that to load on game unpause
        }

        // DEBUG SHIT, will set behind granular prefs later
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
            if (debug_text.Value) {
                int local_y_offset = y_offset.Value;
                DrawText(x_offset.Value, local_y_offset, "Frame #" + frame_idx.ToString(), Color.magenta);
                local_y_offset += font_size.Value + 2;

                DrawText(x_offset.Value, local_y_offset, "X: " + frame_inputX.ToString("N2"), Color.magenta);
                local_y_offset += font_size.Value + 2;
                DrawText(x_offset.Value, local_y_offset, "Y: " + frame_inputY.ToString("N2"), Color.magenta);
                local_y_offset += font_size.Value + 2;

                string actions = "";
                if (frame_fire_pressed) {
                    actions += " FIRE ";
                }
                if (frame_jump_pressed) {
                    actions += " JUMP ";
                }
                if (frame_discard_pressed) {
                    actions += " DISCARD ";
                }
                if (frame_swap_card) {
                    actions += " SWAP ";
                }
                DrawText(x_offset.Value, local_y_offset, actions, Color.magenta);
                local_y_offset += font_size.Value + 2;
            }
        }
    }
}
