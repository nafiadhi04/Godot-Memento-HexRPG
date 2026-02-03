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

		// --- EXPORT VARIABLES ---
		[ExportGroup("Reaction UI")]
		[Export] public Control ReactionPanel;
		[Export] public Label ReactionPromptLabel;
		[Export] public ProgressBar ReactionTimerBar;

		[ExportGroup("Player Stats")]
		[Export] public ProgressBar PlayerHPBar;
		[Export] public ProgressBar PlayerAPBar;
		[Export] public Label HPLabel;

		[ExportGroup("Scoring UI")]
		[Export] public Label ScoreLabel;
		[Export] public Label ComboLabel;

		// --- INTERNAL ---
		private bool _isReactionPhase = false;
		private string _expectedReactionWord = "";

		// [ANTI MACET] Gunakan ini daripada ToSignal
		private TaskCompletionSource<bool> _reactionTaskSource;
		private Tween _timerTween;

		private Button _endTurnBtn;
		private Label _turnLabel; // Variabel yang tadi error
		private Control _combatPanel;
		private Label _apLabel;
		private RichTextLabel _combatLog;
		private LineEdit _commandInput;

		public override void _Ready()
		{
			if (ReactionPanel != null) ReactionPanel.Visible = false;

			_endTurnBtn = GetNodeOrNull<Button>("Control/EndTurnBtn");
			_turnLabel = GetNodeOrNull<Label>("Control/TurnLabel");

			if (_endTurnBtn != null)
			{
				_endTurnBtn.Pressed += () => EmitSignal(SignalName.EndTurnRequested);
				_endTurnBtn.FocusMode = Control.FocusModeEnum.None; // Tombol jangan curi fokus
			}

			SetupCombatUI();

			if (PlayerHPBar != null) PlayerHPBar.Value = PlayerHPBar.MaxValue;
			if (PlayerAPBar != null) PlayerAPBar.Value = PlayerAPBar.MaxValue;

			// Dummy task agar tidak null di awal
			_reactionTaskSource = new TaskCompletionSource<bool>();
			_reactionTaskSource.TrySetResult(false);

			CallDeferred("ConnectToScoreManager");
			_commandInput.FocusMode = Control.FocusModeEnum.All;

		}

		private void SetupCombatUI()
		{
			if (HasNode("Control/CombatPanel"))
			{
				_combatPanel = GetNode<Control>("Control/CombatPanel");
				_combatLog = _combatPanel.GetNodeOrNull<RichTextLabel>("VBoxContainer/CombatLog");
				_commandInput = _combatPanel.GetNodeOrNull<LineEdit>("VBoxContainer/CommandInput");
				_apLabel = _combatPanel.GetNodeOrNull<Label>("VBoxContainer/APLabel");

				if (_apLabel == null) _apLabel = _combatPanel.GetNodeOrNull<Label>("APLabel");

				if (_commandInput != null)
				{
					if (!_commandInput.IsConnected("text_submitted", new Callable(this, MethodName.OnCommandEntered)))
						_commandInput.TextSubmitted += OnCommandEntered;
				}
				_combatPanel.Visible = false;
			}
		}

		private void ConnectToScoreManager()
		{
			if (ScoreManager.Instance != null)
			{
				ScoreManager.Instance.ScoreUpdated += OnScoreUpdated;
				ScoreManager.Instance.ComboUpdated += OnComboUpdated;
				OnScoreUpdated(0);
				OnComboUpdated(0);
			}
		}

		// --- FUNGSI UTAMA: REACTION SYSTEM (ANTI-DEADLOCK) ---
		public async Task<bool> WaitForPlayerReaction(string commandWord, float duration)
		{
			// 1. Reset State
			_isReactionPhase = true;
			_expectedReactionWord = commandWord.ToLower();
			_reactionTaskSource = new TaskCompletionSource<bool>();

			// 2. Setup UI
			ShowCombatPanel(true);

			if (ReactionPanel != null)
			{
				ReactionPanel.Visible = true;
				if (ReactionPromptLabel != null)
					ReactionPromptLabel.Text = $"TYPE: {commandWord.ToUpper()}!";

				if (ReactionTimerBar != null)
				{
					ReactionTimerBar.MaxValue = duration;
					ReactionTimerBar.Value = duration;

					if (_timerTween != null && _timerTween.IsValid())
						_timerTween.Kill();

					_timerTween = CreateTween();
					_timerTween.TweenProperty(
						ReactionTimerBar,
						"value",
						0,
						duration
					).SetTrans(Tween.TransitionType.Linear);

					_timerTween.TweenCallback(
						Callable.From(() => FailReaction("TIMEOUT"))
					);
				}
			}

			// 3. Fokus Input (AWAL)
			if (_commandInput != null)
			{
				_commandInput.Clear();
				_commandInput.GrabFocus();
			}

			GD.Print($"[HUD] WAITING INPUT: '{commandWord}' ({duration}s)");

			// 4. TUNGGU HASIL
			// 4. TUNGGU HASIL
			bool result = await _reactionTaskSource.Task;

			// 🔥 FIX FINAL: FORCE RESET GUI FOCUS
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			GetViewport().GuiReleaseFocus();

			// CallDeferred = dieksekusi SETELAH UI stabil
			_commandInput?.CallDeferred(Control.MethodName.GrabFocus);

			// 5. Cleanup
			_isReactionPhase = false;

			if (_timerTween != null && _timerTween.IsValid())
				_timerTween.Kill();

			if (ReactionPanel != null)
				ReactionPanel.Visible = false;

			if (_commandInput != null)
				_commandInput.Clear();

			return result;
		}


		// --- INPUT HANDLER ---
		private void OnCommandEntered(string text)
		{
			string cleanText = text.Trim().ToLower();

			// KASUS 1: FASE REAKSI (DODGE/PARRY)
			if (_isReactionPhase)
			{
				if (cleanText == _expectedReactionWord)
				{
					LogToTerminal(">>> PERFECT DEFENSE!", Colors.Cyan);
					ResolveReaction(true);
				}
				else
				{
					FailReaction("TYPO");
				}

				if (_commandInput != null) _commandInput.Clear();
				return;
			}

			// KASUS 2: GILIRAN PLAYER BIASA
			EmitSignal(SignalName.CommandSubmitted, cleanText);
			if (_commandInput != null) _commandInput.Clear();
		}

		private void ResolveReaction(bool success)
		{
			if (!_isReactionPhase) return;
			if (_reactionTaskSource != null && !_reactionTaskSource.Task.IsCompleted)
			{
				_reactionTaskSource.TrySetResult(success);
			}
		}

		private void FailReaction(string reason)
		{
			if (_isReactionPhase)
			{
				GD.Print($"[HUD] REACTION FAILED: {reason}");
				ResolveReaction(false);
			}
		}

		// --- ENABLE PLAYER INPUT (Supaya bisa ngetik lagi setelah Parry) ---
		public void EnablePlayerInput()
		{
			// Matikan sisa-sisa fase reaksi musuh
			_isReactionPhase = false;
			if (_timerTween != null && _timerTween.IsValid()) _timerTween.Kill();
			if (_reactionTaskSource != null && !_reactionTaskSource.Task.IsCompleted) _reactionTaskSource.TrySetResult(false);

			if (_combatPanel != null)
			{
				_combatPanel.Visible = true;

				if (_commandInput != null)
				{
					_commandInput.Editable = true;
					_commandInput.Clear();

					// Langsung fokus (Tanpa await process_frame agar tidak ada delay input)
					_commandInput.GrabFocus();
					_commandInput.CaretColumn = 0;
				}
			}
		}

		// --- Helper Methods ---
		public override void _Process(double delta)
		{
			// Paksa fokus hanya saat fase reaksi
			if (_isReactionPhase && ReactionPanel.Visible && _commandInput != null && !_commandInput.HasFocus())
			{
				_commandInput.GrabFocus();
			}
		}

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
		}

		// Update Stats Wrappers
		private void OnScoreUpdated(int newScore) { if (ScoreLabel != null) ScoreLabel.Text = $"SCORE: {newScore:N0}"; }
		private void OnComboUpdated(int newCombo)
		{
			if (ComboLabel != null)
			{
				ComboLabel.Text = $"COMBO: x{newCombo}";
				if (newCombo > 0)
				{
					var t = CreateTween();
					ComboLabel.Scale = new Vector2(1.5f, 1.5f);
					t.TweenProperty(ComboLabel, "scale", Vector2.One, 0.2f);
				}
			}
		}

		public void UpdateHP(int current, int max)
		{
			if (HPLabel != null) HPLabel.Text = $"{current}/{max}";
			if (PlayerHPBar != null)
			{
				PlayerHPBar.MaxValue = max;
				CreateTween().TweenProperty(PlayerHPBar, "value", current, 0.3f).SetTrans(Tween.TransitionType.Quint).SetEase(Tween.EaseType.Out);
				PlayerHPBar.Modulate = ((float)current / max < 0.2f) ? Colors.Red : Colors.White;
			}
		}

		public void UpdateAP(int current, int max)
		{
			if (_apLabel != null) _apLabel.Text = $"AP: {current}/{max}";
			if (PlayerAPBar != null)
			{
				PlayerAPBar.MaxValue = max;
				CreateTween().TweenProperty(PlayerAPBar, "value", current, 0.3f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
			}
		}

		public void SetEndTurnButtonInteractable(bool interactable) { if (_endTurnBtn != null) _endTurnBtn.Disabled = !interactable; }
		public void UpdateTurnLabel(string text) { if (_turnLabel != null) _turnLabel.Text = text; }
	}
}