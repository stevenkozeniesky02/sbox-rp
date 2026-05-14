/// <summary>
/// "Double Paycheck" — for the duration, every <see cref="PaydaySystem"/> tick pays 2x salary.
/// First concrete <see cref="ServerEvent"/> in obsidianrp; Phase C pilot.
/// </summary>
public sealed class DoublePaycheckEvent : ServerEvent
{
	public override string DisplayName => "Double Paycheck";
	public override float DurationSeconds => 300f;  // 5 minutes
	public override float Weight => 1f;

	public override void OnStart( ServerEventSystem system )
	{
		system.IsDoublePaycheck = true;
	}

	public override void OnEnd( ServerEventSystem system )
	{
		system.IsDoublePaycheck = false;
	}
}
