using System;
using System.Threading;
using System.Threading.Tasks;

namespace DreamTech.Leaderboard.League
{
    /// <summary>
    /// Một lượt chạy dùng chung cho mọi người gọi chồng nhau: lượt gửi hàng chờ cúp của <see cref="LeagueSystem"/>, lượt tải bảng của
    /// <see cref="LeagueBoardService"/>.
    ///
    /// <para><b>Huỷ chỉ tác động người gọi.</b> Lượt chạy bằng token RIÊNG, không bằng token của người mở lượt. Mỗi người gọi chờ kết quả
    /// theo token của mình (<see cref="WaitAsync"/>): huỷ thì chỉ người đó rời lượt và nhận huỷ của chính token đó. Token riêng chỉ bị
    /// huỷ khi người chờ cuối cùng rời đi — không còn ai cần kết quả. Trước khi có lớp này, lượt dùng chung chạy bằng token của người
    /// mở lượt: người đó huỷ thì mọi người đang nhập cùng lượt nhận <see cref="OperationCanceledException"/> không phải của mình.</para>
    ///
    /// <para><b>Lượt đã đóng không nhận thêm người.</b> Lượt đóng khi xong, lỗi, hoặc khi người chờ cuối cùng rời đi (đang huỷ). Người gọi
    /// sau đó phải mở lượt mới (<see cref="TryJoin"/> trả false), nên không bao giờ nhập vào một lượt đang dừng hay đọc kết quả cũ.</para>
    ///
    /// <para><b>Luồng.</b> Không tự đổi thread và không dùng <c>ConfigureAwait(false)</c>: phần tiếp theo sau mỗi await quay về context
    /// của nơi gọi (main thread), như mọi phần khác của League.</para>
    /// </summary>
    internal sealed class SharedOperationRun<TResult>
    {
        private readonly object _gate = new object();
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly TaskCompletionSource<TResult> _completion = new TaskCompletionSource<TResult>();
        private int _waiterCount;
        private bool _isClosed;

        /// <summary>Token riêng của lượt — đưa cho việc thật (gọi dịch vụ). Chỉ bị huỷ khi không còn ai chờ.</summary>
        public CancellationToken Token => _cancellation.Token;

        /// <summary>Nhập vào lượt (tính là một người chờ). false = lượt đã đóng, phải mở lượt mới.</summary>
        public bool TryJoin()
        {
            lock (_gate)
            {
                if (_isClosed) return false;
                _waiterCount++;
                return true;
            }
        }

        /// <summary>
        /// Chạy <paramref name="operation"/> bằng <see cref="Token"/> rồi báo kết quả cho mọi người đang chờ. Không bao giờ ném ra ngoài:
        /// lỗi đi tới người chờ qua <see cref="WaitAsync"/>.
        /// </summary>
        public async Task RunAsync(Func<CancellationToken, Task<TResult>> operation)
        {
            try
            {
                TResult result = await operation(Token);
                Close();
                _completion.TrySetResult(result);
            }
            catch (OperationCanceledException) when (Token.IsCancellationRequested)
            {
                // Chỉ xảy ra khi mọi người chờ đã huỷ: không còn ai nhận kết quả.
                Close();
                _completion.TrySetCanceled();
            }
            catch (Exception exception)
            {
                Close();
                _completion.TrySetException(exception);
                // Đánh dấu đã quan sát: mọi người chờ có thể đã rời đi, lỗi không được thành UnobservedTaskException. Ai còn chờ vẫn
                // nhận lỗi này khi await.
                _ = _completion.Task.Exception;
            }
        }

        /// <summary>
        /// Người đã <see cref="TryJoin"/> chờ kết quả theo token CỦA MÌNH. Token đó huỷ trước khi lượt xong thì người này rời lượt và Task
        /// kết thúc ở trạng thái huỷ với chính token đó; lượt vẫn chạy cho những người còn chờ.
        /// </summary>
        public async Task<TResult> WaitAsync(CancellationToken cancellationToken)
        {
            Task<TResult> completion = _completion.Task;
            if (completion.IsCompleted || !cancellationToken.CanBeCanceled) return await completion;

            var cancelled = new TaskCompletionSource<bool>();
            using (cancellationToken.Register(state => ((TaskCompletionSource<bool>)state).TrySetResult(true), cancelled))
            {
                Task finished = await Task.WhenAny(completion, cancelled.Task);
                if (finished != completion)
                {
                    Leave();
                    throw new OperationCanceledException(cancellationToken);
                }
            }
            return await completion;
        }

        /// <summary>Một người chờ đã huỷ. Người cuối cùng rời đi thì đóng lượt và huỷ token riêng.</summary>
        private void Leave()
        {
            lock (_gate)
            {
                if (_isClosed) return;
                _waiterCount--;
                if (_waiterCount > 0) return;
                _isClosed = true;
            }
            _cancellation.Cancel();
        }

        /// <summary>
        /// Đóng TRƯỚC khi báo kết quả: phần tiếp theo của người chờ chạy đồng bộ mà gọi lại lần nữa thì mở lượt mới, không nhập lượt vừa
        /// xong.
        /// </summary>
        private void Close()
        {
            lock (_gate) _isClosed = true;
        }
    }
}
