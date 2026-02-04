using Godot;
using System;
using System.Threading.Tasks;
using MementoTest.Core;

namespace MementoTest.UI
{
	public partial class BattleHUD : CanvasLayer
	{
		// --- SIGNALS ---
		[Signal] public delegate void CommandSubmittedEventHandler(string commandText);
		[Signal] public delegate void EndTurnRequestedEventHandler();
		// (Signal ReactionEnded dihapus karena kita ganti pakai Task yang lebih canggih)

		[Export] public Label ScoreLabel;
		[Export] public Label ComboLabel;

		// --- EXPORT UI ---
		[Export] public Control ReactionPanel;
		[Export] public Label ReactionPromptLabel;
		[Export] public ProgressBar ReactionTimerBar;


	// --- [BARU] PLAYER STATS BARS ---
        [Export] public ProgressBar PlayerHPBar;
        [Export] public ProgressBar PlayerAPBar;
		[Export] public Label HPLabel; 
		
		// --- INTERNAL ---
		private bool _isReactionPhase = false;
		private string _expectedReactionWord = "";

		//  Wadah untuk menunggu hasil input player
		private TaskCompletionSource<bool> _reactionTaskSource;

		private Control _combatPanel;
		private LineEdit _commandInput;
		private RichTextLabel _combatLog;
		private Label _apLabel;
		private Button _endTurnBtn;

		public override void _Ready()
		{
			if (ReactionPanel != null) ReactionPanel.Visible = false;

			_endTurnBtn = GetNodeOrNull<Button>("Control/EndTurnBtn");
			if (_endTurnBtn != null)
				_endTurnBtn.Pressed += () => EmitSignal(SignalName.EndTurnRequested);

			SetupCombatUI();

			if (PlayerHPBar != null) PlayerHPBar.Value = PlayerHPBar.MaxValue;
			if (PlayerAPBar != null) PlayerAPBar.Value = PlayerAPBar.MaxValue;
			CallDeferred("ConnectToScoreManager");
		}
		private void ConnectToScoreManager()
		{
			if (ScoreManager.Instance != null)
			{
				ScoreManager.Instance.ScoreUpdated += OnScoreUpdated;
				ScoreManager.Instance.ComboUpdated += OnComboUpdated;

				// Reset tampilan awal
				OnScoreUpdated(0);
				OnComboUpdated(0);
			}
		}

		private void OnScoreUpdated(int newScore)
		{
			if (ScoreLabel != null) ScoreLabel.Text = $"SCORE: {newScore:N0}"; // N0 biar ada titik (1.000)
		}

		private void OnComboUpdated(int newCombo)
		{
			if (ComboLabel != null)
			{
				ComboLabel.Text = $"COMBO: x{newCombo}";

				// Efek visual sederhana: Warna berubah kalau combo tinggi
				if (newCombo > 5) ComboLabel.Modulate = Colors.Yellow;
				else ComboLabel.Modulate = Colors.White;

				// Animasi kecil (Punch)
				var tween = CreateTween();
				ComboLabel.Scale = new Vector2(1.5f, 1.5f);
				tween.TweenProperty(ComboLabel, "scale", Vector2.One, 0.2f);
			}
		}

		private void SetupCombatUI()
		{
			if (HasNode("Control/CombatPanel"))
			{
				_combatPanel = GetNode<Control>("Control/CombatPanel");
				_combatLog = _combatPanel.GetNodeOrNull<RichTextLabel>("VBoxContainer/CombatLog");
				_commandInput = _combatPanel.GetNodeOrNull<LineEdit>("VBoxContainer/CommandInput");

				// Cari AP Label (Flexible Path)
				_apLabel = _combatPanel.GetNodeOrNull<Label>("VBoxContainer/APLabel");
				if (_apLabel == null) _apLabel = _combatPanel.GetNodeOrNull<Label>("APLabel");

				if (_commandInput != null)
				{
					// Pastikan tidak double connect signal
					if (!_commandInput.IsConnected("text_submitted", new Callable(this, MethodName.OnCommandEntered)))
						_commandInput.TextSubmitted += OnCommandEntered;
				}

				_combatPanel.Visible = false;
			}
		}

		public override void _Process(double delta)
		{
			// Logika Timer & Fokus saat Fase Reaksi
			if (_isReactionPhase && ReactionPanel.Visible)
			{
				// Paksa Fokus ke Input agar player bisa langsung ketik
				if (_commandInput != null && !_commandInput.HasFocus())
					_commandInput.GrabFocus();

				// Hitung Mundur Waktu
				if (ReactionTimerBar != null)
				{
					ReactionTimerBar.Value -= delta;
					if (ReactionTimerBar.Value <= 0)
					{
						FailReaction("TIMEOUT");
					}
				}
			}
		}

		// --- FUNGSI UTAMA (DIPANGGIL MUSUH) ---
		public async Task<bool> WaitForPlayerReaction(string commandWord, float duration)
		{
			// 1. Reset State
			_isReactionPhase = true;
			_expectedReactionWord = commandWord.ToLower();

			// [FIX] Buat Task baru untuk ditunggu
			_reactionTaskSource = new TaskCompletionSource<bool>();

			// 2. Tampilkan UI
			ShowCombatPanel(true);
			if (ReactionPanel != null)
			{
				ReactionPanel.Visible = true;
				if (ReactionPromptLabel != null) ReactionPromptLabel.Text = $"TYPE: {commandWord.ToUpper()}!";
				if (ReactionTimerBar != null)
				{
					ReactionTimerBar.MaxValue = duration;
					ReactionTimerBar.Value = duration;
				}
			}

			// Bersihkan Input & Fokus
			if (_commandInput != null)
			{
				_commandInput.Clear();
				_commandInput.GrabFocus();
			}

			GD.Print($"[HUD] WAITING INPUT: '{commandWord}'");

			// 3. TUNGGU DI SINI SAMPAI ADA HASIL (Anti-Deadlock)
			// Script akan pause di baris ini sampai _reactionTaskSource di-isi nilainya
			bool result = await _reactionTaskSource.Task;

			// 4. Selesai -> Bersihkan UI
			_isReactionPhase = false;
			if (ReactionPanel != null) ReactionPanel.Visible = false;
			if (_commandInput != null) _commandInput.Clear();

			return result;
		}

		// --- INPUT HANDLER ---
		private void OnCommandEntered(string text)
		{
			string cleanText = text.Trim().ToLower();

			// Skenario 1: Fase Reaksi (Dodge/Parry)
			if (_isReactionPhase)
			{
				if (cleanText == _expectedReactionWord)
				{
					LogToTerminal(">>> PERFECT DEFENSE!", Colors.Cyan);
					_reactionTaskSource.TrySetResult(true); // Kirim SUKSES ke Musuh
				}
				else
				{
					FailReaction("TYPO");
				}

				if (_commandInput != null) _commandInput.Clear();
				return; // Stop, jangan proses sebagai command attack
			}

			// Skenario 2: Giliran Player Normal
			EmitSignal(SignalName.CommandSubmitted, cleanText);
			if (_commandInput != null) _commandInput.Clear();
		}

		private void FailReaction(string reason)
		{
			if (_isReactionPhase)
			{
				GD.Print($"[HUD] FAILED: {reason}");
				_reactionTaskSource.TrySetResult(false); // Kirim GAGAL ke Musuh
				_isReactionPhase = false;
			}
		}

		// --- Helper Methods ---
		public void ShowCombatPanel(bool show)
		{
			if (_combatPanel != null)
			{
				_combatPanel.Visible = show;
				if (show && _commandInput != null) _commandInput.GrabFocus();
			}
		}
		public void LogToTerminal(string message, Color color)
		{
			if (_combatLog != null)
			{
				string hexColor = color.ToHtml();
				_combatLog.AppendText($"[color=#{hexColor}]{message}[/color]\n");
			}
			else
			{
				// Fallback jika UI Log belum siap
				GD.Print($"[LOG] {message}");
			}
		}

		private void GrabFocusDeferred()
		{
			if (_commandInput != null) _commandInput.GrabFocus();
		}

		public void SetEndTurnButtonInteractable(bool interactable)
		{
			if (_endTurnBtn != null) _endTurnBtn.Disabled = !interactable;
		}
		public void UpdateTurnLabel(string text) { } // Implementasi label jika ada
		public void UpdateAP(int current, int max)
		{
			// 1. Update Text Label (Cara Lama)
			if (_apLabel != null) _apLabel.Text = $"AP: {current}/{max}";

			// 2. [BARU] Update Progress Bar dengan Animasi
			if (PlayerAPBar != null)
			{
				PlayerAPBar.MaxValue = max;

				// Buat Tween agar bar turun/naik perlahan
				var tween = CreateTween();
				tween.TweenProperty(PlayerAPBar, "value", current, 0.3f)
					.SetTrans(Tween.TransitionType.Sine)
					.SetEase(Tween.EaseType.Out);
			}
		}

		public void UpdateHP(int current, int max)
		{
			// Update Text (Opsional)
			if (HPLabel != null) HPLabel.Text = $"{current}/{max}";

			// Update Progress Bar dengan Animasi
			if (PlayerHPBar != null)
			{
				PlayerHPBar.MaxValue = max;

				var tween = CreateTween();
				tween.TweenProperty(PlayerHPBar, "value", current, 0.3f)
					.SetTrans(Tween.TransitionType.Quint) // Efek hentakan sedikit
					.SetEase(Tween.EaseType.Out);

				// Ubah warna bar jadi merah gelap jika HP kritis (< 20%)
				if ((float)current / max < 0.2f)
				{
					PlayerHPBar.Modulate = Colors.Red;
				}
				else
				{
					PlayerHPBar.Modulate = Colors.White; // Reset warna normal
				}
			}
		}
		public async void EnablePlayerInput()
		{
			if (_combatPanel != null)
			{
				_combatPanel.Visible = true;

				if (_commandInput != null)
				{
					_commandInput.Editable = true;
					_commandInput.Clear();

					// 1. Tunggu 1 frame agar Godot selesai merender perubahan UI turn sebelumnya
					await ToSignal(GetTree(), "process_frame");

					// 2. NUCLEAR RESET: Lepaskan fokus dulu (biar tidak bingung)
					_commandInput.ReleaseFocus();

					// 3. Ambil Fokus
					_commandInput.GrabFocus();

					// 4. Pastikan kursor ada di posisi siap ketik
					_commandInput.CaretColumn = 0;

					// Debug: Pastikan fokus benar-benar diambil
					GD.Print($"[HUD] Input Focus FORCED. Owner: {GetViewport().GuiGetFocusOwner()?.Name}");
				}
			}
		}
	}
}