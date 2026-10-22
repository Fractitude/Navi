namespace Sandbox;

/// <summary>
/// Scene events about a computer's hardware. They run on every client that has the computer (the host, and the players near
/// it), when that client sees the change: on the host as game code installs or removes hardware, on the others when the
/// host's state arrives. Implement it on a component, e.g. to light up a network LED.
/// </summary>
public interface INaviHardwareEvents : ISceneEvent<INaviHardwareEvents>
{
	/// <summary>
	/// Hardware was installed in or removed from <paramref name="computer"/>.
	/// </summary>
	void OnHardwareChanged( NaviComputer computer ) { }

	/// <summary>
	/// <paramref name="computer"/> went online or offline (<see cref="NaviComputer.IsOnline"/>).
	/// </summary>
	void OnNetworkChanged( NaviComputer computer, bool online ) { }
}

/// <summary>
/// Hardware, the IP address and the speakers. What a computer can do beyond its own screen and files depends on the hardware
/// it has (<see cref="Hardware"/>), and each piece of hardware is known by the capabilities it provides
/// (<see cref="NaviHardwareResource.Capabilities"/>): with <see cref="NaviHardware.Network"/> it is online, with
/// <see cref="NaviHardware.Audio"/> it plays sounds. Only game code installs or removes hardware, on the host. Apps read it
/// (<c>Navi.Hardware</c>, <c>Navi.Network</c>, <c>Navi.Audio</c>) and hear about changes (<c>NaviApp.OnHardwareChanged</c>,
/// <c>NaviApp.OnNetworkChanged</c>).
/// </summary>
public sealed partial class NaviComputer
{
	/// <summary>
	/// The hardware installed in this computer. Set in the scene for what a new computer starts with; after that the save
	/// has it, and game code changes it with <see cref="InstallHardware(string)"/> and <see cref="RemoveHardware"/>, which also
	/// tell the apps. Don't add to the list directly.
	/// </summary>
	[Property, Group( "Hardware" )]
	public List<NaviHardwareResource> Hardware { get; set; } = DefaultHardware();

	/// <summary>
	/// What a new computer has: Navi's network card and sound card.
	/// </summary>
	static List<NaviHardwareResource> DefaultHardware() =>
		new[] { NaviHardware.NetworkCard, NaviHardware.SoundCard }.Select( NaviHardware.Find ).Where( x => x is not null ).ToList();

	/// <summary>
	/// <see cref="Hardware"/>, without empty entries or repeats.
	/// </summary>
	public IEnumerable<NaviHardwareResource> InstalledHardware =>
		Hardware?.Where( x => x is not null ).DistinctBy( x => x.Id ) ?? [];

	public bool HasHardware( string id ) => InstalledHardware.Any( x => x.Id == id );

	/// <summary>
	/// Ids of installed hardware the player turned off (<see cref="SetHardwareEnabled"/>). A turned-off piece is still
	/// installed, but provides no capability. Saved. Don't change the list directly.
	/// </summary>
	public List<string> DisabledHardware { get; private set; } = new();

	/// <summary>
	/// Is this hardware turned on? True for hardware that isn't installed, too: only a turned-off piece answers false.
	/// </summary>
	public bool IsHardwareEnabled( string id ) => !DisabledHardware.Contains( id );

	/// <summary>
	/// The installed hardware that is turned on, which is what gives the computer its capabilities.
	/// </summary>
	public IEnumerable<NaviHardwareResource> ActiveHardware => InstalledHardware.Where( x => IsHardwareEnabled( x.Id ) );

	/// <summary>
	/// Does any hardware that is turned on provide <paramref name="capability"/> (<see cref="NaviHardware"/>)?
	/// </summary>
	public bool HasCapability( string capability ) => ActiveHardware.Any( x => x.Provides( capability ) );

	/// <summary>
	/// The computer has hardware that provides <see cref="NaviHardware.Network"/>, turned on or not.
	/// </summary>
	public bool HasNetworkCard => InstalledHardware.Any( x => x.Provides( NaviHardware.Network ) );

	/// <summary>
	/// The computer can reach other computers, the Store and the web: it has hardware that provides
	/// <see cref="NaviHardware.Network"/>, and it is turned on. See <see cref="NaviNetwork"/>.
	/// </summary>
	public bool IsOnline => HasCapability( NaviHardware.Network );

	/// <summary>
	/// Turn an installed piece of hardware on or off. Returns false if it isn't installed or already is that way. Host
	/// only. Turning off a network card takes the computer offline, which apps and the scene hear about like any change.
	/// </summary>
	public bool SetHardwareEnabled( string id, bool enabled )
	{
		if ( !IsAuthority || NaviScriptGuard.Active || !HasHardware( id ) || IsHardwareEnabled( id ) == enabled )
			return false;

		DisabledHardware = enabled ? DisabledHardware.Where( x => x != id ).ToList() : DisabledHardware.Append( id ).ToList();
		Version++;
		return true;
	}

	/// <summary>
	/// Turn every network card on or off: the computer goes online or offline. Returns false if nothing changed. Host only.
	/// </summary>
	public bool SetNetworkEnabled( bool enabled )
	{
		var changed = false;

		foreach ( var card in InstalledHardware.Where( x => x.Provides( NaviHardware.Network ) ).ToList() )
			changed |= SetHardwareEnabled( card.Id, enabled );

		return changed;
	}

	/// <summary>
	/// The user picked Go offline or Go online in the tray's network menu.
	/// </summary>
	[Rpc.Host]
	public void UserSetNetworkEnabled( bool enabled )
	{
		if ( Authorize() )
			SetNetworkEnabled( enabled );
	}

	/// <summary>
	/// The computer can play sounds: it has hardware that provides <see cref="NaviHardware.Audio"/>. Without it, it only beeps.
	/// </summary>
	public bool HasAudio => HasCapability( NaviHardware.Audio );

	/// <summary>
	/// Install a piece of hardware by its id (<see cref="NaviHardwareResource.Id"/>). Returns false if there is no such
	/// hardware or it is already installed. Host only.
	/// </summary>
	public bool InstallHardware( string id ) => InstallHardware( NaviHardware.Find( id ) );

	/// <inheritdoc cref="InstallHardware(string)"/>
	public bool InstallHardware( NaviHardwareResource hardware )
	{
		if ( !IsAuthority || NaviScriptGuard.Active || hardware is null || HasHardware( hardware.Id ) )
			return false;

		Hardware = InstalledHardware.Append( hardware ).ToList();
		DisabledHardware = DisabledHardware.Where( x => x != hardware.Id ).ToList();
		Version++;
		return true;
	}

	/// <summary>
	/// Take a piece of hardware out. Returns false if it isn't installed. Host only.
	/// </summary>
	public bool RemoveHardware( string id )
	{
		if ( !IsAuthority || NaviScriptGuard.Active || !HasHardware( id ) )
			return false;

		Hardware = InstalledHardware.Where( x => x.Id != id ).ToList();
		DisabledHardware = DisabledHardware.Where( x => x != id ).ToList();
		Version++;
		return true;
	}

	/// <summary>
	/// Take on a save's hardware. A save from before Navi had hardware keeps what the scene set up. Hardware this game
	/// doesn't have (an asset that was removed) is left out.
	/// </summary>
	void ApplyHardware( NaviComputerSave save )
	{
		DisabledHardware = save.DisabledHardware?.Distinct().ToList() ?? new();

		if ( save.Hardware is null )
			return;

		var hardware = new List<NaviHardwareResource>();
		foreach ( var id in save.Hardware.Distinct() )
		{
			if ( NaviHardware.Find( id ) is { } found )
				hardware.Add( found );
			else if ( IsAuthority )
				Log.Warning( $"NaviComputer: no hardware with id '{id}', so it is left out" );
		}

		Hardware = hardware;
	}

	// Telling apps and game code. Every client works out the change itself, from its own copy of the state.

	/// <summary>
	/// This client has the computer's hardware as it really is: the host once it has started (and loaded its save), a client
	/// once the host's state came. Before that, what the scene set up would look like a change.
	/// </summary>
	bool hasHardware;

	int hardwareCheckedVersion = -1;
	string deliveredHardware;
	bool deliveredOnline;

	/// <summary>
	/// After any change, see whether the hardware or the network changed, and tell the apps on this client and the scene.
	/// </summary>
	void UpdateHardwareEvents()
	{
		if ( !hasHardware || hardwareCheckedVersion == Version )
			return;

		hardwareCheckedVersion = Version;

		var hardware = string.Join( "\n",
			InstalledHardware.Select( x => IsHardwareEnabled( x.Id ) ? x.Id : $"{x.Id} (off)" ).Order( System.StringComparer.Ordinal ) );
		var online = IsOnline;

		if ( deliveredHardware is null )
		{
			deliveredHardware = hardware;
			deliveredOnline = online;
			return;
		}

		if ( hardware == deliveredHardware )
			return;

		var networkChanged = online != deliveredOnline;
		deliveredHardware = hardware;
		deliveredOnline = online;

		foreach ( var host in appHosts.Values.ToList() )
		{
			host.DeliverHardwareChanged();

			if ( networkChanged )
				host.DeliverNetworkChanged( online );
		}

		Scene.RunEvent<INaviHardwareEvents>( x => x.OnHardwareChanged( this ) );

		if ( networkChanged )
			Scene.RunEvent<INaviHardwareEvents>( x => x.OnNetworkChanged( this, online ) );
	}

	// The IP address.

	/// <summary>
	/// The computer's address on the network, e.g. "10.42.7.19". It is given out the first time the computer starts, kept in
	/// its save, and unique among the computers of the scene and of the save slot (<see cref="NaviSaveData.ReadAddressBook"/>).
	/// It stays the same whether or not the computer is online; only an online computer can be reached at it
	/// (<see cref="NaviNetwork.Reach"/>). Empty on a client until the host's state comes.
	/// </summary>
	public string IpAddress { get; private set; } = "";

	/// <summary>
	/// Give the computer a fixed address, e.g. a server the story points the player to. Returns false if it isn't an address
	/// (<see cref="NaviNetwork.IsAddress"/>) or another computer has it. Host only. It is saved like a given-out one.
	/// </summary>
	public bool SetIpAddress( string address )
	{
		if ( !IsAuthority || NaviScriptGuard.Active || !NaviNetwork.IsAddress( address ) || AddressTaken( address, AddressBook() ) )
			return false;

		if ( IpAddress != address )
		{
			IpAddress = address;
			Version++;
		}

		RecordAddress();
		return true;
	}

	/// <summary>
	/// On the host: keep the address the save gave, unless another computer has it, or give out a new one.
	/// </summary>
	void EnsureIpAddress()
	{
		if ( !IsAuthority )
			return;

		var book = AddressBook();

		// A save that was cut off before its address was written still has it in the address book.
		if ( !NaviNetwork.IsAddress( IpAddress ) && AutoSave
			&& book.FirstOrDefault( x => x.Value == SaveKey && NaviNetwork.IsAddress( x.Key ) ).Key is { } recorded )
		{
			IpAddress = recorded;
			Version++;
		}

		if ( NaviNetwork.IsAddress( IpAddress ) && !AddressTaken( IpAddress, book ) )
		{
			RecordAddress();
			return;
		}

		var address = NaviNetwork.NewAddress( x => AddressTaken( x, book ) );
		if ( address is null )
		{
			Log.Warning( $"NaviComputer: no free IP address for '{GameObject.Name}'" );
			return;
		}

		IpAddress = address;
		Version++;
		RecordAddress();
	}

	/// <summary>
	/// The save slot's address book. A computer that doesn't save (<see cref="AutoSave"/> off) gets a new address each time
	/// it starts, so it isn't recorded there, but it still keeps clear of the recorded ones.
	/// </summary>
	Dictionary<string, string> AddressBook() => NaviSaveData.ReadAddressBook();

	bool AddressTaken( string address, Dictionary<string, string> book ) =>
		Scene.GetAllComponents<NaviComputer>().Any( x => x != this && x.IpAddress == address )
		|| (book.TryGetValue( address, out var owner ) && owner != SaveKey);

	void RecordAddress()
	{
		if ( AutoSave && NaviNetwork.IsAddress( IpAddress ) )
			NaviSaveData.WriteAddress( SaveKey, IpAddress );
	}

	// The speakers. A sound plays from the computer, on every client near it, like the notification sound.

	/// <summary>
	/// The most sounds a computer plays in a second, so an app can't make a racket. Beeps count separately.
	/// </summary>
	public const int MaxSoundsPerSecond = 8;

	/// <summary>
	/// The shortest time between two beeps, in seconds.
	/// </summary>
	const float BeepInterval = 0.2f;

	const int MaxSoundPath = 256;

	readonly Queue<float> recentSounds = new();
	RealTimeSince sinceBeep = BeepInterval;

	/// <summary>
	/// Play a sound (a <c>.sound</c> resource path) from the computer, for everyone near it. Returns false, and plays
	/// nothing, without a sound card (<see cref="HasAudio"/>), for something that isn't a sound path, or past
	/// <see cref="MaxSoundsPerSecond"/>. Host only. Never throws.
	/// </summary>
	public bool PlaySound( string sound, float volume = 1f )
	{
		if ( !IsAuthority || !HasAudio || !IsSoundPath( sound ) || !TakeSoundSlot() )
			return false;

		PlaySoundRpc( sound, System.Math.Clamp( volume, 0f, 1f ) );
		return true;
	}

	/// <summary>
	/// Beep from the computer's own speaker, which every computer has, sound card or not. For telling the player something
	/// finished or needs them. Returns false if it beeped a moment ago. Host only.
	/// </summary>
	public bool Beep()
	{
		if ( !IsAuthority || !TakeBeep() )
			return false;

		BeepRpc();
		return true;
	}

	/// <summary>
	/// An app's <c>audio.play</c>. It plays from the copy of the app the player hears (<see cref="IsVoice"/>), so a sound
	/// plays once however many clients run the app.
	/// </summary>
	internal bool PlayAppSound( NaviWindow window, string sound, float volume )
	{
		if ( !HasAudio || !IsSoundPath( sound ) || !IsVoice( window ) || !TakeSoundSlot() )
			return false;

		volume = float.IsFinite( volume ) ? System.Math.Clamp( volume, 0f, 1f ) : 1f;

		if ( IsAuthority )
		{
			PlaySoundRpc( sound, volume );
		}
		else
		{
			PlayLocalSound( sound, volume );
			UserPlaySound( sound, volume );
		}

		return true;
	}

	/// <summary>
	/// An app's <c>audio.beep</c>. See <see cref="PlayAppSound"/>.
	/// </summary>
	internal bool AppBeep( NaviWindow window )
	{
		if ( !IsVoice( window ) || !TakeBeep() )
			return false;

		if ( IsAuthority )
		{
			BeepRpc();
		}
		else
		{
			PlayLocalBeep();
			UserBeep();
		}

		return true;
	}

	/// <summary>
	/// Is the app's copy on this client the one whose sounds count? A shared app's is the host's. Any other runs on every
	/// client near the screen, and the real one is the seated player's; with nobody in the seat it is the host's.
	/// </summary>
	bool IsVoice( NaviWindow window ) =>
		window.App.Shared || !Networking.IsActive || UserId == default ? IsAuthority : IsUser;

	static bool IsSoundPath( string sound ) =>
		!string.IsNullOrWhiteSpace( sound ) && sound.Length <= MaxSoundPath
		&& sound.EndsWith( ".sound", System.StringComparison.OrdinalIgnoreCase ) && !sound.Contains( ".." );

	bool TakeSoundSlot()
	{
		var now = RealTime.Now;
		while ( recentSounds.Count > 0 && now - recentSounds.Peek() > 1f )
			recentSounds.Dequeue();

		if ( recentSounds.Count >= MaxSoundsPerSecond )
			return false;

		recentSounds.Enqueue( now );
		return true;
	}

	bool TakeBeep()
	{
		if ( sinceBeep < BeepInterval )
			return false;

		sinceBeep = 0;
		return true;
	}

	/// <summary>
	/// The seated player's own copy of an app played a sound there: play it for everyone else.
	/// </summary>
	[Rpc.Host]
	void UserPlaySound( string sound, float volume )
	{
		if ( Rpc.Caller is not { } caller || caller.Id != UserId || !HasAudio || !IsSoundPath( sound ) || !TakeSoundSlot() )
			return;

		using ( Rpc.FilterExclude( caller ) )
			PlaySoundRpc( sound, System.Math.Clamp( volume, 0f, 1f ) );
	}

	[Rpc.Host]
	void UserBeep()
	{
		if ( Rpc.Caller is not { } caller || caller.Id != UserId || !TakeBeep() )
			return;

		using ( Rpc.FilterExclude( caller ) )
			BeepRpc();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlaySoundRpc( string sound, float volume )
	{
		if ( HasAudio && IsSoundPath( sound ) )
			PlayLocalSound( sound, volume );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void BeepRpc() => PlayLocalBeep();

	void PlayLocalSound( string sound, float volume )
	{
		var handle = Sound.Play( sound, WorldPosition );
		if ( handle.IsValid() )
			handle.Volume *= volume;
	}

	// The beep is made here rather than loaded: a square wave, like a PC speaker's.

	const int BeepSampleRate = 44100;
	const float BeepSeconds = 0.12f, BeepFrequency = 988f;

	static short[] beepSamples;

	readonly List<(SoundStream Stream, RealTimeSince Since)> beepStreams = new();

	void PlayLocalBeep()
	{
		beepSamples ??= MakeBeep();

		var stream = new SoundStream( BeepSampleRate, 1 );
		stream.WriteData( beepSamples );

		var handle = stream.Play( 1f, 1f );
		if ( handle.IsValid() )
			handle.Position = WorldPosition;

		beepStreams.Add( (stream, 0) );
	}

	static short[] MakeBeep()
	{
		var samples = new short[(int)(BeepSampleRate * BeepSeconds)];
		var fade = BeepSampleRate / 200;

		for ( var i = 0; i < samples.Length; i++ )
		{
			var high = (int)(i * BeepFrequency * 2 / BeepSampleRate) % 2 == 0;
			var envelope = System.Math.Min( 1f, System.Math.Min( i, samples.Length - 1 - i ) / (float)fade );
			samples[i] = (short)((high ? 1 : -1) * 5000 * envelope);
		}

		return samples;
	}

	// Console commands, for testing. They work on the computer the local player sits at, or else the nearest one, and only
	// on the host: hardware is the game's to install.

	/// <summary>
	/// Log the hardware, network state and IP address of the computer at hand.
	/// </summary>
	[ConCmd( "navi_hardware" )]
	public static void HardwareCommand()
	{
		if ( CommandTarget() is not { } computer )
			return;

		Log.Info( $"{computer.GameObject.Name}: {(computer.IsOnline ? "online" : "offline")}, IP {computer.IpAddress}, "
			+ $"{(computer.HasAudio ? "sound card" : "beep only")}" );

		foreach ( var hardware in computer.InstalledHardware )
			Log.Info( $"  {hardware.Name} ({hardware.Id}){(computer.IsHardwareEnabled( hardware.Id ) ? "" : " [off]")}: "
				+ string.Join( ", ", hardware.Capabilities ?? [] ) );

		Log.Info( $"Hardware there is: {string.Join( ", ", NaviHardware.All.Select( x => x.Id ) )}" );
	}

	/// <summary>
	/// Install hardware in the computer at hand, by its id or file name, e.g. <c>networkcard</c>.
	/// </summary>
	[ConCmd( "navi_hardware_install" )]
	public static void InstallHardwareCommand( string hardware )
	{
		if ( CommandTarget() is not { } computer )
			return;

		var found = FindHardware( hardware );
		Log.Info( found is not null && computer.InstallHardware( found )
			? $"Installed {found.Name} in {computer.GameObject.Name}"
			: $"Couldn't install '{hardware}': no such hardware, or it is already installed" );
	}

	/// <summary>
	/// Take hardware out of the computer at hand, by its id or file name.
	/// </summary>
	[ConCmd( "navi_hardware_remove" )]
	public static void RemoveHardwareCommand( string hardware )
	{
		if ( CommandTarget() is not { } computer )
			return;

		var found = FindHardware( hardware );
		Log.Info( found is not null && computer.RemoveHardware( found.Id )
			? $"Removed {found.Name} from {computer.GameObject.Name}"
			: $"Couldn't remove '{hardware}': it isn't installed" );
	}

	static NaviHardwareResource FindHardware( string name ) =>
		NaviHardware.Find( name )
		?? NaviHardware.All.FirstOrDefault( x =>
			string.Equals( System.IO.Path.GetFileNameWithoutExtension( x.ResourcePath ), name, System.StringComparison.OrdinalIgnoreCase ) );

	static NaviComputer CommandTarget()
	{
		if ( Networking.IsActive && !Networking.IsHost )
		{
			Log.Warning( "Navi: only the host changes a computer's hardware" );
			return null;
		}

		var computers = Game.ActiveScene?.GetAllComponents<NaviComputer>().ToList() ?? [];
		var origin = Game.ActiveScene?.Camera?.WorldPosition ?? Vector3.Zero;
		var computer = computers.FirstOrDefault( x => x.IsSeated )
			?? computers.OrderBy( x => x.WorldPosition.DistanceSquared( origin ) ).FirstOrDefault();

		if ( computer is null )
			Log.Warning( "Navi: there is no computer in the scene" );

		return computer;
	}

	/// <summary>
	/// Close the streams of beeps that finished.
	/// </summary>
	void UpdateBeeps( bool all = false )
	{
		for ( var i = beepStreams.Count - 1; i >= 0; i-- )
		{
			if ( !all && beepStreams[i].Since < 1f )
				continue;

			beepStreams[i].Stream.Dispose();
			beepStreams.RemoveAt( i );
		}
	}
}
