// Every outside link on the page comes from here, so launch day is one edit.
// A link left empty hides the buttons that use it (no dead links).
const SITE = {
  version: "0.1.0",
  repo: "https://github.com/NeonEchoDude/frosty-neon-studio",
  discord: "",     // the server's permanent invite, e.g. "https://discord.gg/xxxxxxx"
  released: false, // true once the first GitHub release exists (until then Download gives the code as a ZIP)
};

const derived = SITE.repo ? {
  download: SITE.released ? SITE.repo + "/releases/latest" : SITE.repo + "/archive/refs/heads/main.zip",
  issues: SITE.repo + "/issues/new/choose",
  guide: SITE.repo + "/blob/main/START_HERE.md",
  license: SITE.repo + "/blob/main/LICENSE",
} : {};
const links = { ...derived, repo: SITE.repo, discord: SITE.discord };

document.querySelectorAll("[data-link]").forEach((el) => {
  const url = links[el.dataset.link];
  if (url) {
    el.href = url;
    if (/^https?:/.test(url)) el.rel = "noopener";
  } else {
    el.hidden = true;
  }
});
// Whole sentences that only make sense with a link ("Ask in the Discord.") hide with it.
document.querySelectorAll("[data-needs]").forEach((el) => {
  if (!links[el.dataset.needs]) el.hidden = true;
});
document.querySelectorAll("[data-version]").forEach((el) => { el.textContent = SITE.version; });
