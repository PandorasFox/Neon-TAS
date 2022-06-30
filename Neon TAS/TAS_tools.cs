using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using MelonLoader;
using UnityEngine;
using HarmonyLib;


namespace MorbedMod {
    public class TAS_tools : MelonMod {
        public static MelonPreferences_Category tas_config;

        public static MelonPreferences_Entry<bool> tas_tools_enabled;
        public static MelonPreferences_Entry<bool> debug_text;
        public static MelonPreferences_Entry<int> x_offset;
        public static MelonPreferences_Entry<int> y_offset;
        public static MelonPreferences_Entry<int> font_size;

        public static bool level_methods_patched = false;
        public static bool input_methods_patched = false;

        public override void OnApplicationStart() {
            GameDataManager.powerPrefs.dontUploadToLeaderboard = true;

            tas_config = MelonPreferences.CreateCategory("TAS Tools");
            tas_tools_enabled = tas_config.CreateEntry("Enabled", false);
            x_offset = tas_config.CreateEntry("X Offset", 30);
            y_offset = tas_config.CreateEntry("Y Offset", 30);
            font_size = tas_config.CreateEntry("Font Size", 20);

            if (tas_tools_enabled.Value) {
                EnableTASTools();
            }
        }

        // patching level start/level end, which then patch input methods for that level and load the level?
        // im not TASing menus (yet) since that shit weird and ILs are more interesting
        // presumably the menu cursor position can be *set directly* and wont require as weird patching?
        // or could just have TAS tools auto-start levels and manage loading inputs per level :think:
        

        private void EnableTASTools() {
            PatchLevelMethods();
        }

        private void DisableTASTools() {
            UnpatchLevelMethods();
        }

        private void PatchLevelMethods() {
            // Apply patches to level start & end that will apply & remove input hook patches
            if (!level_methods_patched) {
                HarmonyLib.Harmony harmony = this.HarmonyInstance;
                harmony.PatchAll(typeof(LevelStart_MechControllerInputHook_Patch));
                harmony.PatchAll(typeof(LevelEnd_OnLevelWinInputHook_Patch));
                level_methods_patched = true;
            }
        }

        private void UnpatchLevelMethods() {
            // Remove patches from level start & end
            if (level_methods_patched) {
                HarmonyLib.Harmony harmony = this.HarmonyInstance;
                harmony.UnpatchSelf();
                level_methods_patched = false;
            }
            // also clean up input patches; don't leave those lingering!
            UnpatchInputMethods();
        }

        class LevelStart_MechControllerInputHook_Patch {
            [HarmonyPatch(typeof(MechController), "ForceSetup")]
            [HarmonyPrefix]
            static void PatchInputHooks() {
                PatchInputMethods();
            }
        }

        class LevelEnd_OnLevelWinInputHook_Patch {
            [HarmonyPatch(typeof(Game), "OnLevelWin")]
            [HarmonyPrefix]
            static void UnpatchInputHooks() {
                UnpatchInputMethods();
            }
        }

        class LevelEnd_OnPlayerDeathInputHook_Patch {
            [HarmonyPatch(typeof(FirstPersonDrifter), "OnPlayerDie")]
            [HarmonyPrefix]
            static void UnpatchInputHooks() {
                UnpatchInputMethods();
            }
        }

        static HarmonyLib.Harmony input_patches_instance;

        private static void PatchInputMethods() {
            if (!input_methods_patched) {
                input_patches_instance = new HarmonyLib.Harmony("Inputs");

                input_patches_instance.PatchAll(typeof(GameInput_GetAxis_Patch));
                input_patches_instance.PatchAll(typeof(GameInput_GetButton_Patch));
                input_patches_instance.PatchAll(typeof(GameInput_GetButtonDown_Patch));
                input_patches_instance.PatchAll(typeof(GameInput_GetButtonUp_Patch));

                input_methods_patched = true;
                frame_idx = 0;
                // grab level name and set that? Load inputs for level into a buffer here?
                // TODO load TAS input stream from.... some folder. Crib this from the GhostRecorder saving maybe?
            }
        }

        private static void UnpatchInputMethods() {
            if (input_methods_patched) {
                input_patches_instance.UnpatchSelf();
                input_methods_patched = false;
            }
        }

        // TODO: patch mouse input out once adjusting camera via OnFixedUpdate is implemented?

        class GameInput_GetAxis_Patch {
            [HarmonyPatch(typeof(GameInput), "GetAxis")]
            [HarmonyPrefix]
            static bool InjectTASInputs(ref float __result, GameInput.GameActions axis, GameInput.InputType inputType = GameInput.InputType.Game) {
                if (inputType != GameInput.InputType.Game) {
                    return true;
                }
                switch(axis) {
                    case GameInput.GameActions.MoveHorizontal: {
                            __result = frame_inputX;
                            return false;
                        }
                    case GameInput.GameActions.MoveVertical: {
                            __result = frame_inputY;
                            return false;
                        }
                    case GameInput.GameActions.SwapCard: {
                            __result = frame_swap_card;
                            return false;
                        }
                    default: return true;
                }
            }
        }

        class GameInput_GetButton_Patch {
            [HarmonyPatch(typeof(GameInput), "GetButton")]
            [HarmonyPrefix]
            static bool InjectTASInputs(ref bool __result, GameInput.GameActions button, GameInput.InputType inputType = GameInput.InputType.Game) {
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
        }

        class GameInput_GetButtonDown_Patch {
            [HarmonyPatch(typeof(GameInput), "GetButtonDown")]
            [HarmonyPrefix]
            static bool InjectTASInputs(ref bool __result, GameInput.GameActions button, GameInput.InputType inputType = GameInput.InputType.Game) {
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
        }

        class GameInput_GetButtonUp_Patch {
            [HarmonyPatch(typeof(GameInput), "GetButtonUp")]
            [HarmonyPrefix]
            static bool InjectTASInputs(ref bool __result, GameInput.GameActions button, GameInput.InputType inputType = GameInput.InputType.Game) {
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

        // NOTE: should also have a way to run every fixedUpdate and record all input states / camera direction
        // NOTE: should not try to record mouse differentials; should have fixed snapping - just fuckin morb the camera direction.
        // NOTE `RM.drifter.m_cameraHolder.eulerAngles.(y|x)` for reading camera; otherwise read GameInput.Get(Axis|Button)

        public override void OnPreferencesSaved() {
            if (tas_tools_enabled.Value) {
                EnableTASTools();
            } else {
                DisableTASTools();
            }
        }

        public static float last_frame_time = 0;
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

        public static float frame_swap_card = 0f;

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

        public static float time_delta = 0f;

        public override void OnFixedUpdate() {
            // check if enabled, if patches attached/etc
            // need to figure out first frame stuff
            if (input_methods_patched) {
                time_delta = Time.fixedDeltaTime;
                frame_inputX = 0f;
                frame_inputY = 1f;
                setJump(true);

                // RM.drifter.m_cameraHolder.eulerAngles.(y|x)

                ++frame_idx;
            }
            

            // note: should have a keyword/action for 'pause game and hand back input control to user'
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

        public override void OnGUI() {
            if (input_methods_patched && debug_text.Value) {
                int local_y_offset = y_offset.Value;
                DrawText(x_offset.Value, local_y_offset, "Frame #" + frame_idx.ToString() + " (" + time_delta.ToString("N4") + "s)", Color.magenta);
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
                if (frame_swap_card != 0f) {
                    actions += " SWAP ";
                }
                DrawText(x_offset.Value, local_y_offset, actions, Color.magenta);
            }
        }
    }
}
