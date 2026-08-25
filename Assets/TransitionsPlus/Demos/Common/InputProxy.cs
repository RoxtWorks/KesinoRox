using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace TransitionsPlusDemos {

    static class InputProxy {

        public static void SetupEventSystem() {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            var eventSystem = EventSystem.current;
            if (eventSystem == null) return;
            var standaloneModule = eventSystem.GetComponent<StandaloneInputModule>();
            if (standaloneModule != null) {
                Object.Destroy(standaloneModule);
                if (eventSystem.GetComponent<InputSystemUIInputModule>() == null) {
                    eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
                }
            }
#endif
        }
    }
}
