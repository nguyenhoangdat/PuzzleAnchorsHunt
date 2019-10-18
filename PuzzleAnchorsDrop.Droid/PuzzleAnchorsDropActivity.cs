using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Support.V7.App;
using Android.Views;
using Android.Widget;
using Google.AR.Sceneform.Rendering;
using Google.AR.Core;
using Google.AR.Sceneform;
using Google.AR.Sceneform.UX;
using Java.Util.Concurrent;
using PuzzleAnchors.Common;
using Microsoft.Azure.SpatialAnchors;
using System.Collections.Concurrent;
using Android.Util;

namespace PuzzleAnchorsDrop.Droid
{
    [Activity(Label = "PuzzleAnchorsDropActivity")]
    public class PuzzleAnchorsDropActivity : AppCompatActivity
    {
        #region Fields

        private ArFragment arFragment;
        private ArSceneView sceneView;
        private Button exitButton;
        private TextView textView;
        private Button createButton;
        private EditText anchorNumInput;
        private TextView editTextInfo;
        private AzureSpatialAnchorsManager cloudAnchorManager;

        private static Material failedColor;
        private static Material foundColor;
        private static Material readyColor;
        private static Material savedColor;

        private string feedbackText;
        private readonly object renderLock = new object();
        private GameStep currentStep = GameStep.Start;
        private readonly ConcurrentDictionary<string, AnchorVisual> anchorVisuals = new ConcurrentDictionary<string, AnchorVisual>();
        private AnchorSharingServiceClient anchorSharingServiceClient;
        private string anchorId = string.Empty;

        #endregion

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            this.SetContentView(Resource.Layout.activity_anchors);

            this.arFragment = (ArFragment)this.SupportFragmentManager.FindFragmentById(Resource.Id.ux_fragment);
            this.arFragment.TapArPlane += (sender, args) => this.OnTapArPlaneListener(args.HitResult, args.Plane, args.MotionEvent);

            this.sceneView = this.arFragment.ArSceneView;

            this.exitButton = (Button)this.FindViewById(Resource.Id.mainMenu);
            this.exitButton.Click += ExitButton_Click;
            this.textView = (TextView)this.FindViewById(Resource.Id.textView);
            this.textView.Visibility = ViewStates.Visible;
            this.createButton = (Button)this.FindViewById(Resource.Id.createButton);
            this.createButton.Click += this.OnCreateButtonClicked;
            this.anchorNumInput = (EditText)this.FindViewById(Resource.Id.anchorNumText);
            this.editTextInfo = (TextView)this.FindViewById(Resource.Id.editTextInfo);

            this.EnableCorrectUIControls();

            Scene scene = this.sceneView.Scene;
            scene.Update += (_, args) =>
            {
                // Pass frames to Spatial Anchors for processing.
                this.cloudAnchorManager?.Update(this.sceneView.ArFrame);
            };

            // Initialize the colors.
            MaterialFactory.MakeOpaqueWithColor(this, new Color(Android.Graphics.Color.Red)).GetAsync().ContinueWith(materialTask => failedColor = (Material)materialTask.Result);
            MaterialFactory.MakeOpaqueWithColor(this, new Color(Android.Graphics.Color.Green)).GetAsync().ContinueWith(materialTask => savedColor = (Material)materialTask.Result);
            MaterialFactory.MakeOpaqueWithColor(this, new Color(Android.Graphics.Color.Yellow)).GetAsync().ContinueWith(materialTask =>
            {
                readyColor = (Material)materialTask.Result;
                foundColor = readyColor;
            });
        }

        protected override void OnResume()
        {
            base.OnResume();

            // ArFragment of Sceneform automatically requests the camera permission before creating the AR session,
            // so we don't need to request the camera permission explicitly.
            // This will cause onResume to be called again after the user responds to the permission request.
            if (!SceneformHelper.HasCameraPermission(this))
            {
                return;
            }

            if (this.sceneView?.Session is null && !SceneformHelper.TrySetupSessionForSceneView(this, this.sceneView))
            {
                // Exception will be logged and SceneForm will handle any ARCore specific issues.
                this.Finish();
                return;
            }

            if (string.IsNullOrWhiteSpace(AccountDetails.SpatialAnchorsAccountId) || AccountDetails.SpatialAnchorsAccountId == "Set me"
                    || string.IsNullOrWhiteSpace(AccountDetails.SpatialAnchorsAccountKey) || AccountDetails.SpatialAnchorsAccountKey == "Set me")
            {
                Toast.MakeText(this, $"\"Set {AccountDetails.SpatialAnchorsAccountId} and {AccountDetails.SpatialAnchorsAccountKey} in {nameof(AccountDetails)}.cs\"", ToastLength.Long)
                        .Show();

                this.Finish();
                return;
            }

            if (string.IsNullOrEmpty(AccountDetails.AnchorSharingServiceUrl) || AccountDetails.AnchorSharingServiceUrl == "Set me")
            {
                Toast.MakeText(this, $"Set the {AccountDetails.AnchorSharingServiceUrl} in {nameof(AccountDetails)}.cs", ToastLength.Long)
                        .Show();

                this.Finish();
                return;
            }

            this.anchorSharingServiceClient = new AnchorSharingServiceClient(AccountDetails.AnchorSharingServiceUrl);

            this.UpdateStatic();
        }

        private void UpdateStatic()
        {
            new Handler().PostDelayed(() =>
            {
                switch (this.currentStep)
                {
                    case GameStep.Start:
                        break;

                    case GameStep.CreateAnchor:
                        this.textView.Text = this.feedbackText;
                        break;

                    case GameStep.LocateAnchor:
                        if (!string.IsNullOrEmpty(this.anchorId))
                        {
                            this.textView.Text = "searching for\n" + this.anchorId;
                        }
                        break;

                    case GameStep.SavingAnchor:
                        this.textView.Text = "saving...";
                        break;

                    case GameStep.EnterAnchorNumber:
                        break;
                }

                this.UpdateStatic();
            }, 500);
        }

        private void EnableCorrectUIControls()
        {
            switch (this.currentStep)
            {
                case GameStep.Start:
                    this.textView.Visibility = ViewStates.Visible;
                    this.createButton.Visibility = ViewStates.Visible;
                    this.editTextInfo.Visibility = ViewStates.Visible;
                    this.anchorNumInput.Visibility = ViewStates.Visible;
                    this.SupportActionBar.Hide();
                    break;

                case GameStep.CreateAnchor:
                    this.textView.Visibility = ViewStates.Visible;
                    this.createButton.Visibility = ViewStates.Gone;
                    this.editTextInfo.Visibility = ViewStates.Gone;
                    this.anchorNumInput.Visibility = ViewStates.Gone;
                    break;

                case GameStep.LocateAnchor:
                    this.textView.Visibility = ViewStates.Visible;
                    this.createButton.Visibility = ViewStates.Gone;
                    this.editTextInfo.Visibility = ViewStates.Gone;
                    this.anchorNumInput.Visibility = ViewStates.Gone;
                    break;

                case GameStep.SavingAnchor:
                    this.textView.Visibility = ViewStates.Visible;
                    this.createButton.Visibility = ViewStates.Gone;
                    this.editTextInfo.Visibility = ViewStates.Gone;
                    this.anchorNumInput.Visibility = ViewStates.Gone;
                    break;

                case GameStep.EnterAnchorNumber:
                    this.textView.Visibility = ViewStates.Visible;
                    this.createButton.Visibility = ViewStates.Gone;
                    this.editTextInfo.Visibility = ViewStates.Gone;
                    this.anchorNumInput.Visibility = ViewStates.Gone;
                    break;
            }
        }

        public void OnCreateButtonClicked(object sender, EventArgs args)
        {
            this.textView.Text = "Scan your environment and place an anchor";
            this.DestroySession();

            this.cloudAnchorManager = new AzureSpatialAnchorsManager(this.sceneView.Session);

            this.cloudAnchorManager.OnSessionUpdated += (_, sessionUpdateArgs) =>
            {
                SessionStatus status = sessionUpdateArgs.Status;

                if (this.currentStep == GameStep.CreateAnchor)
                {
                    float progress = status.RecommendedForCreateProgress;
                    if (progress >= 1.0)
                    {
                        if (this.anchorVisuals.TryGetValue(string.Empty, out AnchorVisual visual))
                        {
                            //Transition to saving...
                            this.TransitionToSaving(visual);
                        }
                        else
                        {
                            this.feedbackText = "Tap somewhere to place an anchor.";
                        }
                    }
                    else
                    {
                        this.feedbackText = $"Progress is {progress:0%}";
                    }
                }
            };

            this.cloudAnchorManager.StartSession();
            this.currentStep = GameStep.CreateAnchor;
            this.EnableCorrectUIControls();
        }

        private void TransitionToSaving(AnchorVisual visual)
        {
            Log.Debug("ASADemo:", "transition to saving");
            this.currentStep = GameStep.SavingAnchor;
            this.EnableCorrectUIControls();
            Log.Debug("ASADemo", "creating anchor");
            CloudSpatialAnchor cloudAnchor = new CloudSpatialAnchor();
            visual.CloudAnchor = cloudAnchor;
            cloudAnchor.LocalAnchor = visual.LocalAnchor;

            this.cloudAnchorManager.CreateAnchorAsync(cloudAnchor)
                .ContinueWith(async cloudAnchorTask =>
                {
                    try
                    {
                        CloudSpatialAnchor anchor = await cloudAnchorTask;

                        string anchorId = anchor.Identifier;
                        Log.Debug("ASADemo:", "created anchor: " + anchorId);
                        visual.SetColor(savedColor);
                        this.anchorVisuals[anchorId] = visual;
                        this.anchorVisuals.TryRemove(string.Empty, out _);

                        Log.Debug("ASADemo", "recording anchor with web service");
                        Log.Debug("ASADemo", "anchorId: " + anchorId);
                        SendAnchorResponse response = await this.anchorSharingServiceClient.SendAnchorIdAsync(anchorId, anchorNumInput.Text);
                        this.AnchorPosted(response.AnchorNumber);
                    }
                    catch (CloudSpatialException ex)
                    {
                        this.CreateAnchorExceptionCompletion($"{ex.Message}, {ex.ErrorCode}");
                    }
                    catch (Exception ex)
                    {
                        this.CreateAnchorExceptionCompletion(ex.Message);
                        visual.SetColor(failedColor);
                    }
                });
        }

        private void CreateAnchorExceptionCompletion(string message)
        {
            this.textView.Text = message;
            this.currentStep = GameStep.Start;
            this.cloudAnchorManager.StopSession();
            this.cloudAnchorManager = null;
            this.EnableCorrectUIControls();
        }

        private void ExitButton_Click(object sender, EventArgs e)
        {
            lock (this.renderLock)
            {
                this.DestroySession();

                this.Finish();
            }
        }

        private void AnchorPosted(string anchorNumber)
        {
            this.RunOnUiThread(() =>
            {
                this.textView.Text = "Anchor Number: " + anchorNumber;
                this.currentStep = GameStep.Start;
                this.cloudAnchorManager.StopSession();
                this.cloudAnchorManager = null;
                this.ClearVisuals();
                this.EnableCorrectUIControls();
            });
        }

        private void ClearVisuals()
        {
            foreach (AnchorVisual visual in this.anchorVisuals.Values)
            {
                visual.Destroy();
            }

            this.anchorVisuals.Clear();
        }

        private void OnTapArPlaneListener(HitResult hitResult, Plane plane, MotionEvent motionEvent)
        {
            if (this.currentStep == GameStep.CreateAnchor)
            {
                if (!this.anchorVisuals.ContainsKey(string.Empty))
                {
                    this.CreateAnchor(hitResult);
                }
            }
        }

        private Anchor CreateAnchor(HitResult hitResult)
        {
            AnchorVisual visual = new AnchorVisual(hitResult.CreateAnchor());
            visual.SetColor(readyColor);
            visual.AddToScene(this.arFragment);
            this.anchorVisuals[string.Empty] = visual;

            return visual.LocalAnchor;
        }

        private void DestroySession()
        {
            if (this.cloudAnchorManager != null)
            {
                this.cloudAnchorManager.StopSession();
                this.cloudAnchorManager = null;
            }

            this.StopWatcher();

            this.ClearVisuals();
        }
        private void StopWatcher()
        {
            if (this.cloudAnchorManager != null)
            {
                this.cloudAnchorManager.StopLocating();
            }
        }
    }
}