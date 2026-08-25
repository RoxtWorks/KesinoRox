using UnityEngine;

namespace TransitionsPlusDemos {

    public class EventSystemSetup : MonoBehaviour {

        void OnEnable() {
            InputProxy.SetupEventSystem();
        }
    }
}
