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
		[Signal]
		public delegate void ReactionSuccessEventHandler();


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
		private bool _isReactionPhase;
		public bool IsBusy { get; private set; }

		private string _expectedReactionWord = "";

		//  Wadah untuk menunggu hasil input player
		private TaskCompletionSource<bool> _reactionTaskSource;

		private Tween _timerTween;

		private Control _combatPanel;
		private LineEdit _commandInput;
		private RichTextLabel _combatLog;
		private Label _apLabel;
		private Button _endTurnBtn;
		private TaskCompletionSource<bool> _reactionTcs;
		private bool _isWaitingReaction = false;
		private double _reactionStartTime;
		public float LastReactionTime { get; private set; }

		private string _expectedWord = "";
		private bool _reactionActive = false;



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
			_commandInput.TextChanged += OnCommandInputTextChanged;

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
		public async Task<bool> WaitForPlayerReaction(string expectedWord, float timeLimit)
		{
			// =============================
			// 1. CANCEL PREVIOUS REACTION (SAFE)
			// =============================
			if (_reactionTaskSource != null && !_reactionTaskSource.Task.IsCompleted)
			{
				_reactionTaskSource.TrySetResult(false);
			}

			_reactionTaskSource = new TaskCompletionSource<bool>();

			if (_timerTween != null && _timerTween.IsValid())
				_timerTween.Kill();

			// =============================
			// 2. INIT STATE
			// =============================
			_isReactionPhase = true;
			_expectedReactionWord = expectedWord.ToLower();
			_reactionStartTime = Time.GetUnixTimeFromSystem();

			// =============================
			// 3. SETUP UI
			// =============================
			ShowCombatPanel(true);

			if (ReactionPanel == null)
			{
				GD.PrintErr("[HUD] ReactionPanel NULL!");
				return false;
			}

			ReactionPanel.Visible = true;

			if (ReactionPromptLabel != null)
			{
				ReactionPromptLabel.Text = $"TYPE: {expectedWord.ToUpper()}!";
			}

			if (ReactionTimerBar != null)
			{
				ReactionTimerBar.MaxValue = timeLimit;
				ReactionTimerBar.Value = timeLimit;

				_timerTween = CreateTween();
				_timerTween.TweenProperty(
					ReactionTimerBar,
					"value",
					0,
					timeLimit
				).SetTrans(Tween.TransitionType.Linear);

				_timerTween.TweenCallback(Callable.From(() =>
				{
					if (_reactionTaskSource != null && !_reactionTaskSource.Task.IsCompleted)
					{
						GD.Print("[HUD] REACTION TIMEOUT");
						FailReaction("TIMEOUT");
					}
				}));
			}

			// =============================
			// 4. INPUT PREPARATION (FOCUS SAFE)
			// =============================
			if (_commandInput != null)
			{
				_commandInput.Visible = true;
				_commandInput.Editable = true;
				_commandInput.Clear();
				_commandInput.Modulate = Colors.White;

				// tunggu 1 frame agar UI siap sepenuhnya
				await ToSignal(GetTree(), "process_frame");

				_commandInput.GrabFocus();
				GD.Print($"[HUD] Input Focus FORCED. Owner: {_commandInput.Name}");
			}

			GD.Print($"[HUD] WAITING INPUT: '{expectedWord}'");

			// =============================
			// 5. WAIT RESULT
			// =============================
			bool result = await _reactionTaskSource.Task;

			// =============================
			// 6. CLEANUP (FINAL & CONSISTENT)
			// =============================
			_isReactionPhase = false;

			if (_timerTween != null && _timerTween.IsValid())
				_timerTween.Kill();

			if (ReactionPanel != null)
				ReactionPanel.Visible = false;

			if (_commandInput != null)
			{
				_commandInput.Clear();
				_commandInput.Modulate = Colors.White;
				_commandInput.ReleaseFocus();
			}

			if (result)
			{
				EmitSignal(SignalName.ReactionSuccess);
			}

			return result;
		}


		// --- INPUT HANDLER ---
		private void OnCommandEntered(string text)
		{
			string cleanText = text.Trim().ToLower();
			double now = Time.GetUnixTimeFromSystem();
			LastReactionTime = (float)(now - _reactionStartTime);

			// =============================
			// REACTION PHASE (parry / dodge)
			// =============================
			if (_isReactionPhase)
			{
				if (cleanText == _expectedReactionWord)
				{
					LogToTerminal(">>> PERFECT DEFENSE!", Colors.Cyan);
					_reactionTaskSource?.TrySetResult(true);
				}
				else
				{
					FailReaction("TYPO");
				}

				_commandInput.Clear();
				return;
			}

			// =============================
			// NORMAL PLAYER COMMAND
			// =============================
			EmitSignal(SignalName.CommandSubmitted, cleanText);
			_commandInput.Clear();
		}


		private void OnCommandInputTextChanged(string newText)
		{
			if (!_isReactionPhase) return;

			if (string.IsNullOrEmpty(newText))
			{
				_commandInput.Modulate = Colors.White;
				return;
			}

			string expected = _expectedReactionWord;

			for (int i = 0; i < newText.Length; i++)
			{
				if (i >= expected.Length || newText[i] != expected[i])
				{
					_commandInput.Modulate = Colors.IndianRed;
					return;
				}
			}

			// sejauh ini benar
			_commandInput.Modulate = Colors.LimeGreen;
		}


		private void FailReaction(string reason)
		{
			if (!_isReactionPhase) return;

			GD.Print($"[HUD] REACTION FAILED: {reason}");

			_isReactionPhase = false;

			if (_reactionTaskSource != null && !_reactionTaskSource.Task.IsCompleted)
			{
				_reactionTaskSource.TrySetResult(false);
			}

			if (ReactionPromptLabel != null)
				ReactionPromptLabel.Text = "FAILED!";

			if (_commandInput != null)
				_commandInput.Modulate = Colors.IndianRed;
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

		private void CompleteReaction(bool success)
		{
			if (!_reactionTcs.Task.IsCompleted)
			{
				_reactionTcs.SetResult(success);
			}
		}

		private void ForceCancelReaction()
		{
			if (_reactionTcs != null && !_reactionTcs.Task.IsCompleted)
			{
				_reactionTcs.SetResult(false);
			}

			_isWaitingReaction = false;
			_commandInput.Text = "";
			_commandInput.ReleaseFocus();
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