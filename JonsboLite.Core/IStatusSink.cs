namespace JonsboLite;

/// <summary>
/// Somewhere to surface the current readings for the user, updated about once a second
/// while the panel itself keeps receiving the full frame rate.
///
/// Implemented by the Windows tray icon. A Linux StatusNotifierItem, an MQTT publisher or
/// a status file would all fit here without the send loop knowing.
/// </summary>
public interface IStatusSink
{
    void UpdateStatus(Readings readings, bool connected, string model);
}
