using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace DynamicEdge.Services;

public sealed class NotificationItem
{
    public string App = "", Title = "", Body = "";
    public ImageSource? Icon;
}

/// Чтение уведомлений Windows через UserNotificationListener.
/// Windows разрешает это только приложениям с package identity (MSIX) и capability userNotificationListener.
public sealed class NotificationService
{
    readonly Dispatcher _d = Dispatcher.CurrentDispatcher;
    UserNotificationListener? _l;
    public bool Available { get; private set; }
    public string Status { get; private set; } = "Не инициализировано";
    public event Action<NotificationItem>? Received;

    public async Task InitAsync()
    {
        if (!Core.Native.IsPackaged())
        {
            Status = "Недоступно: Windows отдаёт уведомления только приложениям из MSIX-пакета. Установите .msix.";
            return;
        }
        try
        {
            _l = UserNotificationListener.Current;
            var access = await _l.RequestAccessAsync();
            if (access != UserNotificationListenerAccessStatus.Allowed)
            {
                Status = "Доступ не разрешён: Параметры → Конфиденциальность и защита → Доступ к уведомлениям.";
                return;
            }
            _l.NotificationChanged += OnChanged;
            Available = true;
            Status = "Работает";
        }
        catch (Exception ex) { Status = "Ошибка: " + ex.Message; Core.SettingsService.Log("Notif init: " + ex); }
    }

    async void OnChanged(UserNotificationListener sender, UserNotificationChangedEventArgs e)
    {
        try
        {
            if (e.ChangeKind != UserNotificationChangedKind.Added) return;
            var n = sender.GetNotification(e.UserNotificationId);
            if (n == null) return;
            var item = new NotificationItem { App = n.AppInfo?.DisplayInfo?.DisplayName ?? "" };
            var binding = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
            if (binding != null)
            {
                var texts = binding.GetTextElements().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
                if (texts.Count > 0) item.Title = texts[0];
                if (texts.Count > 1) item.Body = string.Join(" ", texts.Skip(1));
            }
            try
            {
                var logo = n.AppInfo?.DisplayInfo?.GetLogo(new Windows.Foundation.Size(64, 64));
                if (logo != null)
                {
                    using var rs = await logo.OpenReadAsync();
                    using var ms = new MemoryStream();
                    await rs.AsStreamForRead().CopyToAsync(ms);
                    item.Icon = ImageUtil.FromBytes(ms.ToArray());
                }
            }
            catch { }
            _d.BeginInvoke(() => Received?.Invoke(item));
        }
        catch (Exception ex) { Core.SettingsService.Log("Notif changed: " + ex.Message); }
    }
}
