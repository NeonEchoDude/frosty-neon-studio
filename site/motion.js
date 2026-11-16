// Gentle reveal of the how-it-works steps as they scroll into view.
// Does nothing when the visitor asks for reduced motion, or in browsers without
// IntersectionObserver: the steps are then simply visible.
if (window.IntersectionObserver && !matchMedia("(prefers-reduced-motion: reduce)").matches) {
  document.documentElement.classList.add("js-motion");
  const seen = new IntersectionObserver((entries) => {
    entries.forEach((e) => {
      if (e.isIntersecting) { e.target.classList.add("is-in"); seen.unobserve(e.target); }
    });
  }, { rootMargin: "0px 0px -10% 0px" });
  document.addEventListener("DOMContentLoaded", () => {
    document.querySelectorAll(".reveal").forEach((el) => seen.observe(el));
  });
}
