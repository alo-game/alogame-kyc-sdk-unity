using System;
using System.Collections.Generic;
using UnityEngine;

namespace Alogame.KycSdk.Internal
{
    /// <summary>
    /// Marshals native callbacks onto the Unity main thread.
    ///
    /// Android needs this: AndroidJavaProxy invokes C# on whichever Java thread
    /// called it, which for this SDK is the Android UI thread — a different
    /// thread from Unity's main thread. Touching almost any Unity API from there
    /// throws, and a game's OnResult handler will certainly touch one.
    ///
    /// iOS does not strictly need it (the Swift shim already hops to the main
    /// queue, which is Unity's main thread on iOS) but routes through it anyway,
    /// so both platforms have identical delivery semantics and a bug can never
    /// be "only on one platform because of where the callback ran".
    ///
    /// A hidden MonoBehaviour draining a queue in Update, rather than
    /// SynchronizationContext.Post: the captured context depends on which thread
    /// Show() happened to be called from, and Unity's context implementation has
    /// varied across versions. This has no such dependency.
    /// </summary>
    internal sealed class AlogameKycMainThreadDispatcher : MonoBehaviour
    {
        private static AlogameKycMainThreadDispatcher _instance;
        private static readonly Queue<Action> _pending = new Queue<Action>();

        /// <summary>
        /// Created eagerly at startup rather than lazily on first Enqueue: the
        /// first Enqueue may well arrive on the Android UI thread, and
        /// GameObject construction off the main thread throws.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null) return;
            var go = new GameObject("AlogameKycMainThreadDispatcher");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<AlogameKycMainThreadDispatcher>();
        }

        internal static void Enqueue(Action action)
        {
            if (action == null) return;
            lock (_pending)
            {
                _pending.Enqueue(action);
            }
        }

        private void Update()
        {
            // Drain into a local list under the lock, then run outside it: a
            // handler that calls back into Enqueue (a game calling Show() again
            // from OnResult, say) would otherwise deadlock or mutate mid-iteration.
            Action[] batch;
            lock (_pending)
            {
                if (_pending.Count == 0) return;
                batch = _pending.ToArray();
                _pending.Clear();
            }

            foreach (var action in batch)
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    // One game handler throwing must not stop the rest of the
                    // batch, and must not kill this dispatcher for the session.
                    Debug.LogError("[AlogameKycSdk] listener threw: " + e);
                }
            }
        }
    }
}
