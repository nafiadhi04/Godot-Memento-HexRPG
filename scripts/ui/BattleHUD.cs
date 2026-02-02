using Godot;
using System;
using System.Threading.Tasks;

namespace MementoTest.UI
{
	public partial class BattleHUD : CanvasLayer
	{
		// --- SIGNALS ---
		[Signal] public delegate void ReactionEndedEventHandler(bool success);
		[Signal] public delegate void EndTurnRequestedEventHandler();
		[Signal] public delegate void CommandSubmittedEventHandler(string commandText);

		// --- EXPORT VARIABLES ---
		[Export] public Control ReactionPanel;
		[Export] public Label ReactionPromptLabel;
		[Export] public ProgressBar ReactionTimerBar;


		// --- PRIVATE VARIABLES ---
		private bool _isReactionPhase = false;
		private string _expectedReactionWord = "";

		// Komponen UI
		private Button _endTurnBtn;
		private Label _turnLabel;
		private Control _combatPanel;
		private Label _apLabel;
		private RichTextLabel _combatLog;
		private LineEdit _commandInput;

		public override void _Ready()
		{
			// Sembunyikan panel reaksi saat mulai
			if (ReactionPanel != null) ReactionPanel.Visible = false;

			// Ambil node dasar
			_endTurnBtn = GetNodeOrNull<Button>("Control/EndTurnBtn");
			_turnLabel = GetNodeOrNull<Label>("Control/TurnLabel");

			if (_endTurnBtn != null)
				_endTurnBtn.Pressed += () => EmitSignal(SignalName.EndTurnRequested);

			// Setup Combat Panel & Input
			SetupCombatUI();
		}

		private void SetupCombatUI()
		{
			if (HasNode("Control/CombatPanel"))
			{
				_combatPanel = GetNode<Control>("Control/CombatPanel");

				// Cari Input Box dan Log di dalam hierarki
				// Menggunakan GetNodeOrNull agar tidak crash jika susunan scene berbeda sedikit
				if (_combatPanel.HasNode("VBoxContainer/CombatLog"))
					_combatLog = GetNode<RichTextLabel>("Control/CombatPanel/VBoxContainer/CombatLog");

				if (_combatPanel.HasNode("VBoxContainer/CommandInput"))
				{
					_commandInput = GetNode<LineEdit>("Control/CombatPanel/VBoxContainer/CommandInput");
					// Hubungkan signal input
					if (!_commandInput.IsConnected("text_submitted", new Callable(this, MethodName.OnCommandEntered)))
					{
						_commandInput.TextSubmitted += OnCommandEntered;
					}
				}

				// Cari AP Label
				if (_combatPanel.HasNode("VBoxContainer/APLabel"))
					_apLabel = GetNode<Label>("Control/CombatPanel/VBoxContainer/APLabel");
				else if (_combatPanel.HasNode("APLabel"))
					_apLabel = GetNode<Label>("Control/CombatPanel/APLabel");

				_combatPanel.Visible = false;
			}
		}

		public override void _Process(double delta)
		{
			// Hanya jalankan jika FASE REAKSI AKTIF
			if (_isReactionPhase && ReactionPanel.Visible)
			{
				// 1. [PERBAIKAN UTAMA] Paksa Fokus Input Box!
				// Jika kursor lepas dari kotak input, tarik balik secara paksa.
				if (_commandInput != null && !_commandInput.HasFocus())
				{
					_commandInput.GrabFocus();
				}

				// 2. Kurangi Waktu
				ReactionTimerBar.Value -= delta;

				// 3. Cek Timeout
				if (ReactionTimerBar.Value <= 0)
				{
					FailReaction("TIMEOUT");
				}
			}
		}
		// --- FUNGSI UTAMA: REACTION SYSTEM (ASYNC) ---
		public async Task<bool> WaitForPlayerReaction(string commandWord, float duration)
		{
			// 1. Reset State
			_isReactionPhase = false;
			ShowCombatPanel(true);
			if (_commandInput != null)
			{
				_commandInput.Clear();
				_commandInput.ReleaseFocus();
			}

			_expectedReactionWord = commandWord.ToLower();

			// 2. Setup UI
			if (ReactionPanel != null)
			{
				ReactionPanel.Visible = true;
				ReactionPromptLabel.Text = $"TYPE: {commandWord.ToUpper()}!";
				ReactionTimerBar.MaxValue = duration;
				ReactionTimerBar.Value = duration;
			}

			// Tunggu 1 frame visual biar aman (Mencegah glitch)
			await ToSignal(GetTree(), "process_frame");

			// 3. Mulai Fase Reaksi
			_isReactionPhase = true;
			if (_commandInput != null) _commandInput.GrabFocus();

			GD.Print($"[HUD] WAITING INPUT: '{commandWord}' for {duration}s");

			// 4. TUNGGU SIGNAL (Ini kuncinya!)
			// Script ini akan PAUSE di sini sampai signal 'ReactionEnded' ditembakkan
			var result = await ToSignal(this, SignalName.ReactionEnded);

			// 5. Cleanup setelah signal diterima
			if (ReactionPanel != null) ReactionPanel.Visible = false;
			_isReactionPhase = false;
			if (_commandInput != null) _commandInput.Clear();

			// Ambil hasil true/false dari signal
			return (bool)result[0];
		}

		// --- INPUT HANDLER ---
		private void OnCommandEntered(string text)
		{
			string cleanText = text.Trim().ToLower();

			// SKENARIO 1: Fase Reaksi (Dodge/Parry)
			if (_isReactionPhase)
			{
				GD.Print($"[HUD INPUT CHECK] Expected: {_expectedReactionWord} | Got: {cleanText}");

				if (cleanText == _expectedReactionWord)
				{
					// SUKSES -> Tembak Signal TRUE
					LogToTerminal(">>> PERFECT DEFENSE!", Colors.Cyan);
					EmitSignal(SignalName.ReactionEnded, true);
				}
				else
				{
					// TYPO -> Tembak Signal FALSE
					FailReaction("TYPO");
				}

				if (_commandInput != null) _commandInput.Clear();
				return; // Stop di sini
			}

			// SKENARIO 2: Fase Normal (Attack Player)
			EmitSignal(SignalName.CommandSubmitted, cleanText);
			if (_commandInput != null) _commandInput.Clear();
		}

		private void FailReaction(string reason)
		{
			if (_isReactionPhase) // Cek biar gak double call
			{
				GD.Print($"[HUD] REACTION FAILED: {reason}");
				_isReactionPhase = false;

				if (ReactionPanel != null) ReactionPanel.Visible = false;

				// GAGAL -> Tembak Signal FALSE
				EmitSignal(SignalName.ReactionEnded, false);
			}
		}

		// --- PUBLIC HELPER METHODS ---

		public void SetEndTurnButtonInteractable(bool interactable)
		{
			if (_endTurnBtn != null)
			{
				_endTurnBtn.Disabled = !interactable;
				_endTurnBtn.Text = interactable ? "END TURN" : "ENEMY TURNING...";
			}
		}

		public void UpdateTurnLabel(string text)
		{
			if (_turnLabel != null) _turnLabel.Text = text;
		}

		public void UpdateAP(int current, int max)
		{
			if (_apLabel != null) _apLabel.Text = $"AP: {current}/{max}";
		}

		public void LogToTerminal(string message, Color color)
		{
			if (_combatLog != null)
			{
				string hexColor = color.ToHtml();
				_combatLog.AppendText($"[color=#{hexColor}]{message}[/color]\n");
			}
		}

		public void ShowCombatPanel(bool show)
		{
			if (_combatPanel != null)
			{
				_combatPanel.Visible = show;
				if (show && _commandInput != null)
				{
					_commandInput.GrabFocus();
				}
			}
		}
	}
}