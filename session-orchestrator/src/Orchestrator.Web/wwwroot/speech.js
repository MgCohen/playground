// Dictation for free-text fields with the Web Speech API. Without speech support, the mic buttons stay hidden.
(function () {
  var Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
  if (!Recognition) return;
  document.documentElement.classList.add("speech");
  var active = null;

  function stop() {
    if (!active) return;
    active.recognition.stop();
  }

  document.addEventListener("click", function (e) {
    var button = e.target.closest("[data-speech-for]");
    if (!button) return;
    var field = document.getElementById(button.getAttribute("data-speech-for"));
    if (!field) return;
    if (active) {
      var same = active.button === button;
      stop();
      if (same) return;
    }
    var recognition = new Recognition();
    recognition.lang = document.documentElement.lang || navigator.language;
    recognition.interimResults = false;
    recognition.continuous = false;
    recognition.onresult = function (event) {
      var text = "";
      for (var i = event.resultIndex; i < event.results.length; i++) {
        if (event.results[i].isFinal) text += event.results[i][0].transcript;
      }
      text = text.trim();
      if (!text) return;
      field.value = field.value ? field.value.replace(/\s*$/, " ") + text : text;
      field.dispatchEvent(new Event("input", { bubbles: true }));
    };
    recognition.onend = function () {
      button.classList.remove("listening");
      button.removeAttribute("aria-pressed");
      if (active && active.recognition === recognition) active = null;
    };
    active = { button: button, recognition: recognition };
    button.classList.add("listening");
    button.setAttribute("aria-pressed", "true");
    recognition.start();
  });
})();

// Blazor calls orchSpeech.focus when an answer sheet opens: it puts the cursor in the field, and with voice it starts the mic.
window.orchSpeech = {
  focus: function (id, voice) {
    var field = document.getElementById(id);
    if (!field) return;
    field.focus();
    if (!voice) return;
    var button = document.querySelector('[data-speech-for="' + id + '"]');
    if (button && document.documentElement.classList.contains("speech")) button.click();
  }
};
