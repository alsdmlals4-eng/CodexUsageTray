"use strict";

const assert = require("node:assert/strict");
const { classifyTransientError } = require("../../browser-extension/recovery-watchdog.js");
const {
  findSafeRecoveryCandidate,
  findTransientErrorSurface,
  isSafeRetryControl
} = require("../../browser-extension/recovery-dom.js");

function control(label, parentElement = null) {
  return {
    textContent: label,
    parentElement,
    getAttribute(name) {
      return name === "aria-label" ? label : null;
    }
  };
}

function element(text, parentElement = null) {
  return {
    textContent: text,
    parentElement
  };
}

function root({ controls = [], surfaces = [] } = {}) {
  return {
    querySelectorAll(selector) {
      if (selector === "button, [role='button']") {
        return controls;
      }
      if (selector === "[role='alert'], [role='status'], [data-testid*='error']") {
        return surfaces;
      }
      return [];
    }
  };
}

assert.equal(isSafeRetryControl(control("다시 시도")), true);
assert.equal(isSafeRetryControl(control("Retry")), true);
assert.equal(isSafeRetryControl(control("Try again")), true);
assert.equal(isSafeRetryControl(control("Regenerate")), true);
assert.equal(isSafeRetryControl(control("확인")), false);
assert.equal(isSafeRetryControl(control("Continue")), false);

const timeoutBox = element("메시지 전송 시간이 초과되었습니다. 다시 시도해 주세요");
const retryInside = control("다시 시도", timeoutBox);
let candidate = findSafeRecoveryCandidate(
  root({ controls: [retryInside] }),
  classifyTransientError);
assert.equal(candidate?.control, retryInside);
assert.equal(candidate?.surface, timeoutBox);

const unrelatedBox = element("일반 설정 영역");
const retryOutside = control("Retry", unrelatedBox);
candidate = findSafeRecoveryCandidate(
  root({ controls: [retryOutside], surfaces: [timeoutBox] }),
  classifyTransientError);
assert.equal(candidate, null, "retry control outside the transient error surface must not be selected");

const nestedError = element("A network error occurred");
const nestedWrapper = element("A network error occurred", nestedError);
const nestedRetry = control("Try again", nestedWrapper);
candidate = findSafeRecoveryCandidate(
  root({ controls: [nestedRetry] }),
  classifyTransientError);
assert.equal(candidate?.control, nestedRetry);
assert.equal(candidate?.surface, nestedWrapper);

const alertSurface = element("Something went wrong while generating a response");
assert.equal(
  findTransientErrorSurface(root({ surfaces: [alertSurface] }), classifyTransientError),
  alertSurface);
assert.equal(
  findTransientErrorSurface(root({ surfaces: [element("승인이 필요합니다")] }), classifyTransientError),
  null);

console.log("PASS recovery DOM safety contracts");
