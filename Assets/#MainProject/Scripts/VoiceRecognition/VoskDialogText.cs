using UnityEngine;
using UnityEngine.UI;
using System.Text.RegularExpressions;

public class VoskDialogText : MonoBehaviour 
{
    public VoskSpeechToText VoskSpeechToText;
    public Text DialogText;

    // --- English Regex Triggers ---
    
    // Basic interaction
    Regex hi_regex = new Regex(@"hello|hi");
    Regex who_regex = new Regex(@"who are you");
    Regex pass_regex = new Regex(@"(ok|good|let's go|yes)");
    Regex help_regex = new Regex(@"(help|hint)");

    // The Characters (Wolf, Goat, Cabbage)
    Regex goat_regex = new Regex(@"(goat|take the goat|move the goat)");
    Regex wolf_regex = new Regex(@"(wolf|take the wolf|move the wolf)");
    Regex cabbage_regex = new Regex(@"(cabbage|take the cabbage|move the cabbage)");

    // Moving items BACK (Right to Left)
    Regex goat_back_regex = new Regex(@"(return the goat|goat back|bring the goat back)");
    Regex wolf_back_regex = new Regex(@"(return the wolf|wolf back|bring the wolf back)");
    Regex cabbage_back_regex = new Regex(@"(return the cabbage|cabbage back|bring the cabbage back)");

    // Moving the Farmer only
    Regex forward_regex = new Regex(@"(cross|go over|move|cross the river)");
    Regex back_regex = new Regex(@"(go back|return|come back)");

    // State of the game objects (true = Left Bank, false = Right Bank)
    bool goat_left;
    bool wolf_left;
    bool cabbage_left;
    bool man_left;

    void Awake()
    {
        //VoskSpeechToText.OnTranscriptionResult += OnTranscriptionResult;
        ResetState();
    }

    void ResetState()
    {
        goat_left = true;
        wolf_left = true;
        cabbage_left = true;
        man_left = true;
    }

    // Logic to check if you won or lost
    void CheckState() {
        // Lose Conditions
        if (goat_left == wolf_left && goat_left != man_left) {
            AddFinalResponse("The wolf ate the goat! Start over.");
            return;
        }
        if (goat_left == cabbage_left && goat_left != man_left) {
            AddFinalResponse("The goat ate the cabbage! Start over.");
            return;
        }
        
        // Win Condition (Everything is on the right/false side)
        if (!goat_left && !wolf_left && !cabbage_left && !man_left) {
            AddFinalResponse("Great job! You solved it. Let's play again!");
            return;
        }

        // Game Continues
        AddResponse("Okay, what's next?");
    }

    // Fixed 'Say' method to prevent crashing on Windows
    void Say(string response)
    {
        // This logs to the Unity Console instead of trying to run the Mac 'say' command
        Debug.Log("Game Says: " + response);
    }

    void AddFinalResponse(string response) {
        Say(response);
        DialogText.text = response + "\n";
        ResetState();
    }

    void AddResponse(string response) {
        Say(response);
        DialogText.text = response + "\n\n";

        // Display current positions (Left or Right)
        DialogText.text += "Farmer is on the " + (man_left ? "Left" : "Right") + "\n";
        DialogText.text += "Wolf is on the " + (wolf_left ? "Left" : "Right") + "\n";
        DialogText.text += "Goat is on the " + (goat_left ? "Left" : "Right") + "\n";
        DialogText.text += "Cabbage is on the " + (cabbage_left ? "Left" : "Right") + "\n";

        DialogText.text += "\n";
    }

    // private void OnTranscriptionResult(string obj)
    // {
    //     Debug.Log("Recognized: " + obj);
    //     var result = new RecognitionResult(obj);
        
    //     foreach (RecognizedPhrase p in result.Phrases)
    //     {
    //         // 1. Greetings / Help
    //         if (hi_regex.IsMatch(p.Text))
    //         {
    //             AddResponse("Hello there!");
    //             return;
    //         }
    //         if (who_regex.IsMatch(p.Text))
    //         {
    //             AddResponse("I am a robot teacher.");
    //             return;
    //         }
    //         if (pass_regex.IsMatch(p.Text))
    //         {
    //             AddResponse("Excellent.");
    //             return;
    //         }
    //         if (help_regex.IsMatch(p.Text))
    //         {
    //             AddResponse("You have to figure it out yourself!");
    //             return;
    //         }

    //         // 2. Moving Items BACK (Right -> Left)
    //         if (goat_back_regex.IsMatch(p.Text)) {
    //             if (goat_left == true) {
    //                 AddResponse("The goat is already on the left bank.");
    //             } else if (man_left == true) {
    //                 AddResponse("The farmer is on the left bank, he can't reach the goat.");
    //             } else {
    //                 goat_left = true;
    //                 man_left = true;
    //                 CheckState();
    //             }
    //             return;
    //         }

    //         if (wolf_back_regex.IsMatch(p.Text)) {
    //             if (wolf_left == true) {
    //                 AddResponse("The wolf is already on the left bank.");
    //             } else if (man_left == true) {
    //                 AddResponse("The farmer is on the left bank, he can't reach the wolf.");
    //             } else {
    //                 wolf_left = true;
    //                 man_left = true;
    //                 CheckState();
    //             }
    //             return;
    //         }

    //         if (cabbage_back_regex.IsMatch(p.Text)) {
    //             if (cabbage_left == true) {
    //                 AddResponse("The cabbage is already on the left bank.");
    //             } else if (man_left == true) {
    //                 AddResponse("The farmer is on the left bank, he can't reach the cabbage.");
    //             } else {
    //                 cabbage_left = true;
    //                 man_left = true;
    //                 CheckState();
    //             }
    //             return;
    //         }

    //         // 3. Moving Items FORWARD (Left -> Right)
    //         if (goat_regex.IsMatch(p.Text)) {
    //             if (goat_left == false) {
    //                 AddResponse("The goat is already on the right bank.");
    //             } else if (man_left == false) {
    //                 AddResponse("The farmer is on the right bank, he can't reach the goat.");
    //             } else {
    //                 goat_left = false;
    //                 man_left = false;
    //                 CheckState();
    //             }
    //             return;
    //         }

    //         if (wolf_regex.IsMatch(p.Text)) {
    //             if (wolf_left == false) {
    //                 AddResponse("The wolf is already on the right bank.");
    //             } else if (man_left == false) {
    //                 AddResponse("The farmer is on the right bank, he can't reach the wolf.");
    //             } else {
    //                 wolf_left = false;
    //                 man_left = false;
    //                 CheckState();
    //             }
    //             return;
    //         }

    //         if (cabbage_regex.IsMatch(p.Text)) {
    //             if (cabbage_left == false) {
    //                 AddResponse("The cabbage is already on the right bank.");
    //             } else if (man_left == false) {
    //                 AddResponse("The farmer is on the right bank, he can't reach the cabbage.");
    //             } else {
    //                 cabbage_left = false;
    //                 man_left = false;
    //                 CheckState();
    //             }
    //             return;
    //         }

    //         // 4. Moving just the Farmer
    //         if (forward_regex.IsMatch(p.Text)) {
    //             if (man_left == false) {
    //                 AddResponse("The farmer is already on the right bank.");
    //             } else {
    //                 man_left = false;
    //                 CheckState();
    //             }
    //             return;
    //         }
        
    //         if (back_regex.IsMatch(p.Text)) {
    //             if (man_left == true) {
    //                 AddResponse("The farmer is already on the left bank.");
    //             } else {
    //                 man_left = true;
    //                 CheckState();
    //             }
    //             return;
    //         }
    //     }
        
    //     // If speech was detected but didn't match any commands
    //     if (result.Phrases.Length > 0 && result.Phrases[0].Text != "") {
    //         AddResponse("I don't understand that command.");
    //     }
    // }
}