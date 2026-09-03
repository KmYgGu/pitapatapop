using System.Collections.Generic;
using Spine;
using Spine.Unity;
using UnityEngine;
using Animation = Spine.Animation;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// Spine 애니메이션을 <b>이름으로</b> 재생하는 한 곳. 없는 동작을 어떻게 메울지도 여기서 정한다.
    ///
    /// <b>왜 Mecanim 이 아니라 코드 제어인가</b>(2026-08-25 사용자 지시로 되돌림): Animator 를
    /// 쓰면 캐릭터마다 컨트롤러가 하나씩 필요하고, 상태 기계에 없는 전이는 <b>조용히 아무 일도
    /// 안 한다</b>. 캐릭터가 늘어날수록 관리 비용만 늘고 얻는 게 없다.
    ///
    /// <b>없는 동작은 그 캐릭터의 idle 로 메운다</b>(2026-08-30 사용자 기획). 새 캐릭터는 시간
    /// 사정상 <c>1.idle</c> 하나만 만들어 넣는 경우가 많은데, 그런 캐릭터에게 <c>2.win</c> 을
    /// 시키면 아무것도 안 하고 멈춰 있게 된다.
    ///
    /// <b>⚠ 예전엔 '다른 캐릭터(Rabrith)의 같은 이름 동작'을 태웠는데 그건 없앴다.</b>
    /// Spine 타임라인은 뼈·슬롯을 이름이 아니라 <b>번호</b>로 가리켜서, 리깅이 다르면 엉뚱한
    /// 파츠가 움직인다. 예전 주석은 "같은 리그에 그림만 바꿔 끼우는 전제"라고 적어뒀는데
    /// <b>그 전제가 실제로 깨졌다</b> - 새로 들어온 셋은 뼈 수부터 다르다
    /// (Rabrith 33 / 미스틱 42 / 카우펜스 43 / 라미아 37). 그래서 자기 idle 로 돌린다.
    ///
    /// <b>나중에 동작을 추가하면 그날부터 그게 나온다</b> - 이름은 재생할 때마다 찾으므로
    /// 애셋만 다시 넣으면 코드는 손댈 게 없다.
    /// </summary>
    public static class SpinePlayback
    {
        /// <summary>대사·연출에서 기본으로 쓰는 대기 동작. <b>메울 때 쓰는 동작이기도 하다.</b></summary>
        public const string Idle = "1.idle";

        /// <summary>이겼을 때.</summary>
        public const string Win = "2.win";

        /// <summary>때리기 직전 자세.</summary>
        public const string ReadyAttack = "4.readyattack";

        /// <summary>내려찍고 난 자세.</summary>
        public const string AttackDone = "5.attackdone";

        /// <summary>찾아낸 동작과, 그게 <b>대신 나온 idle 인지</b>.</summary>
        public readonly struct Resolved
        {
            public readonly Animation Clip;

            /// <summary>부탁한 동작이 없어서 idle 로 메웠다.</summary>
            public readonly bool FellBackToIdle;

            public Resolved(Animation clip, bool fellBackToIdle)
            {
                Clip = clip;
                FellBackToIdle = fellBackToIdle;
            }

            public bool Found => Clip != null;
        }

        // 같은 캐릭터의 같은 동작을 놓쳤다고 매 프레임 떠들면 로그가 묻힌다. 조합마다 한 번만.
        private static readonly HashSet<string> warned = new HashSet<string>();

        /// <summary>
        /// 이름으로 동작을 찾는다. 그 캐릭터에게 없으면 <b>그 캐릭터의 idle</b> 을 돌려준다.
        /// idle 마저 없으면 아무것도 안 돌려준다 - 엉뚱한 동작보다 멈춘 게 낫다.
        /// </summary>
        public static Resolved Resolve(SkeletonData data, string animationName)
        {
            if (data == null || string.IsNullOrEmpty(animationName))
                return default;

            var clip = data.FindAnimation(animationName);
            if (clip != null)
                return new Resolved(clip, false);

            // idle 을 부탁했는데 없으면 메울 것도 없다.
            if (animationName == Idle)
            {
                WarnOnce(data, animationName, "idle 자체가 없습니다");
                return default;
            }

            var idle = data.FindAnimation(Idle);
            if (idle == null)
            {
                WarnOnce(data, animationName, $"'{Idle}' 도 없어서 메울 수 없습니다");
                return default;
            }

            WarnOnce(data, animationName, $"'{Idle}' 로 대신합니다");
            return new Resolved(idle, true);
        }

        /// <summary>
        /// 이름으로 재생한다. 없으면 idle 로 메운다.
        ///
        /// <b>전환은 섞지 않는다</b>(2026-08-27 사용자 지시). 기본 <paramref name="mixDuration"/>
        /// 이 0이라 이전 동작이 곧바로 끊긴다 - 섞으면 공격을 끝낸 자세와 대기 자세가 겹쳐 보여서
        /// "잔상이 남는" 것처럼 읽힌다.
        /// </summary>
        /// <returns>실제로 재생했으면 true.</returns>
        public static bool Play(SkeletonAnimation player, string animationName, bool loop,
            float mixDuration = 0f)
        {
            if (player == null)
                return false;

            return Play(player.AnimationState, player.Skeleton?.Data, animationName, loop,
                        0, mixDuration);
        }

        /// <summary>
        /// 트랙을 골라 재생한다. 대사창처럼 <c>SkeletonGraphic</c> 옆에 붙은 재생기를 직접
        /// 들고 있는 자리에서 쓴다.
        ///
        /// <b>idle 로 메웠으면 반복으로 돌린다</b> - 원래 한 번만 재생할 동작(공격 등)을
        /// 대신하러 온 idle 을 한 번만 틀면 끝나고 굳어버려서, 서 있어야 할 캐릭터가 멈춘다.
        /// </summary>
        public static bool Play(Spine.AnimationState state, SkeletonData data, string animationName,
            bool loop, int track = 0, float mixDuration = 0f)
        {
            if (state == null)
                return false;

            var resolved = Resolve(data, animationName);
            if (!resolved.Found)
                return false;

            // 스켈레톤 데이터에 동작 쌍마다 섞는 시간이 미리 박혀 있을 수 있다. 그 기본값을
            // 여기서 눌러두고(DefaultMix), 실제로 시작하는 트랙에도 다시 지정한다 -
            // <b>둘 다 해야</b> 확실히 안 섞인다(쌍별 설정이 DefaultMix 를 이기기 때문).
            float mix = Mathf.Max(0f, mixDuration);
            if (state.Data != null)
                state.Data.DefaultMix = mix;

            var entry = state.SetAnimation(track, resolved.Clip, loop || resolved.FellBackToIdle);
            if (entry != null)
                entry.MixDuration = mix;

            return true;
        }

        private static void WarnOnce(SkeletonData data, string animationName, string tail)
        {
            string key = $"{data.Name}/{animationName}";
            if (!warned.Add(key))
                return;

            Debug.LogWarning($"[SpinePlayback] '{data.Name}' 에 '{animationName}' 동작이 없어 {tail}.");
        }
    }
}
